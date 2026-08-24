using System;
using System.Collections.Generic;
using JellyTrack.Plugin.Services;
using MediaBrowser.Model.Entities;
using Xunit;

namespace JellyTrack.Plugin.Tests;

public sealed class PlaybackMediaStreamCacheTests
{
    [Fact]
    public void LoadsAndCachesStreamsForAnItem()
    {
        var cache = new PlaybackMediaStreamCache();
        var itemId = Guid.NewGuid();
        var loadCount = 0;

        IReadOnlyList<MediaStream> Loader()
        {
            loadCount++;
            return new List<MediaStream>
            {
                new() { Type = MediaStreamType.Audio, Index = 1, Title = "French AC3" },
                new() { Type = MediaStreamType.Subtitle, Index = 2, Title = "French SRT" }
            };
        }

        var first = cache.GetStreams(itemId, Loader);
        var second = cache.GetStreams(itemId, Loader);

        Assert.Equal(1, loadCount);
        Assert.Equal(2, first.Count);
        Assert.Same(first, second);
    }

    [Fact]
    public void InvalidateRemovesItemFromCache()
    {
        var cache = new PlaybackMediaStreamCache();
        var itemId = Guid.NewGuid();
        var loadCount = 0;

        IReadOnlyList<MediaStream> Loader()
        {
            loadCount++;
            return new List<MediaStream> { new() { Type = MediaStreamType.Video, Index = 0 } };
        }

        cache.GetStreams(itemId, Loader);
        cache.Invalidate(itemId);
        cache.GetStreams(itemId, Loader);

        Assert.Equal(2, loadCount);
    }
}
