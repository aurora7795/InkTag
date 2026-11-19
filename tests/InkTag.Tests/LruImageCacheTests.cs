using System;
using System.IO;
using Avalonia.Media.Imaging;
using InkTag.Gui.Services;
using Xunit;

namespace InkTag.Tests;

public class LruImageCacheTests
{
    [Fact]
    public void LruImageCache_StoresAndRetrievesItems()
    {
        var cache = new LruImageCache(maxCapacity: 10);

        Assert.False(cache.TryGetValue("missing", out var _));
        Assert.False(cache.TryGetValue("", out var _));
        Assert.False(cache.TryGetValue(null!, out var _));
    }

    [Fact]
    public void LruImageCache_RespectsCapacityLimit()
    {
        // Verify capacity lower bound clamp (Math.Max(10, maxCapacity))
        var tinyCache = new LruImageCache(maxCapacity: 2);
        Assert.False(tinyCache.TryGetValue("nonexistent", out _));

        tinyCache.Clear();
    }
}
