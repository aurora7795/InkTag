using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace InkTag.Gui.Services;

/// <summary>
/// Thread-safe size-bounded LRU (Least Recently Used) cache for Avalonia <see cref="Bitmap"/> instances.
/// </summary>
/// <remarks>
/// This cache manages bounded in-memory caching of decoded cover bitmaps during browsing and scraping.
/// <para>
/// <b>Eviction &amp; Disposal Semantics:</b>
/// When the cache reaches its capacity limit, evicted entries have their strong references removed from the
/// cache dictionary, allowing the unmanaged Skia/Avalonia bitmap resources to be safely reclaimed by the
/// .NET garbage collector and finalizer once visual elements detach from them.
/// Explicit <see cref="IDisposable.Dispose"/> is deliberately NOT invoked upon eviction: in v0.13.0, aggressive
/// disposal on eviction caused native crashes during UI layout passes (<c>Image.MeasureOverride</c>) when
/// an evicted bitmap was still bound to an active visual tree element.
/// </para>
/// </remarks>
public class LruImageCache
{
    private readonly int _maxCapacity;
    private readonly object _lock = new();
    private readonly Dictionary<string, Bitmap> _cache;
    private readonly LinkedList<string> _lruList;

    /// <summary>
    /// Initializes a new instance of the <see cref="LruImageCache"/> class with a specified maximum capacity.
    /// </summary>
    /// <param name="maxCapacity">The maximum number of bitmaps to retain in the cache (minimum 10).</param>
    public LruImageCache(int maxCapacity = 60)
    {
        _maxCapacity = Math.Max(10, maxCapacity);
        _cache = new Dictionary<string, Bitmap>(_maxCapacity);
        _lruList = new LinkedList<string>();
    }

    /// <summary>
    /// Attempts to retrieve a cached bitmap by key and refreshes its LRU position if found.
    /// </summary>
    /// <param name="key">The cache lookup key.</param>
    /// <param name="bitmap">The cached bitmap instance if found, or null.</param>
    /// <returns>True if the bitmap was found in the cache; otherwise, false.</returns>
    public bool TryGetValue(string key, out Bitmap? bitmap)
    {
        if (string.IsNullOrEmpty(key))
        {
            bitmap = null;
            return false;
        }

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out bitmap))
            {
                _lruList.Remove(key);
                _lruList.AddLast(key);
                return true;
            }
        }

        bitmap = null;
        return false;
    }

    /// <summary>
    /// Stores a bitmap in the cache, evicting the least recently used entry if maximum capacity has been reached.
    /// </summary>
    /// <param name="key">The cache key identifying the bitmap.</param>
    /// <param name="bitmap">The decoded Avalonia bitmap to cache.</param>
    public void Set(string key, Bitmap bitmap)
    {
        if (string.IsNullOrEmpty(key) || bitmap == null) return;

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out _))
            {
                _lruList.Remove(key);
            }
            else if (_cache.Count >= _maxCapacity)
            {
                var oldestNode = _lruList.First;
                if (oldestNode != null)
                {
                    string oldestKey = oldestNode.Value;
                    _lruList.RemoveFirst();
                    _cache.Remove(oldestKey);
                }
            }

            _cache[key] = bitmap;
            _lruList.AddLast(key);
        }
    }

    /// <summary>
    /// Clears all entries from the cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lruList.Clear();
        }
    }
}
