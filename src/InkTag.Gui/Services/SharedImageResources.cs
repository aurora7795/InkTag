using System;
using System.Collections.Concurrent;
using System.Net.Http;

namespace InkTag.Gui.Services;

/// <summary>
/// Process-wide shared GUI image and perceptual hash caches to avoid per-window/per-call cache duplication.
/// </summary>
public static class SharedImageResources
{
    private const int MaxHashCacheEntries = 500;

    public static ArchiveCoverService ArchiveCoverService { get; } = new();
    public static LruImageCache RemoteThumbnailCache { get; } = new(120);
    public static HttpClient ThumbnailHttpClient { get; } = CreateThumbnailHttpClient();

    private static readonly ConcurrentDictionary<string, ulong> _hashCache = new();
    private static readonly ConcurrentQueue<string> _hashInsertionOrder = new();

    private static HttpClient CreateThumbnailHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
        {
            client.DefaultRequestHeaders.Add("User-Agent", "InkTag/1.0 (ThumbnailLoader)");
        }
        return client;
    }

    public static bool TryGetCachedHash(string url, out ulong hash)
    {
        return _hashCache.TryGetValue(url, out hash);
    }

    public static void SetCachedHash(string url, ulong hash)
    {
        if (string.IsNullOrEmpty(url) || hash == 0) return;

        if (_hashCache.TryAdd(url, hash))
        {
            _hashInsertionOrder.Enqueue(url);
            while (_hashCache.Count > MaxHashCacheEntries && _hashInsertionOrder.TryDequeue(out string? oldest))
            {
                _hashCache.TryRemove(oldest, out _);
            }
        }
        else
        {
            _hashCache[url] = hash;
        }
    }
}
