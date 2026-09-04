using System.Collections.Concurrent;
using MediaBrowser.Model.Entities;

namespace JellyTrack.Plugin.Services;

public sealed class PlaybackMediaStreamCache
{
    public const int MaxCacheEntries = 1000;
    private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<Guid, CachedStreams> _cache = new();

    public IReadOnlyList<MediaStream> GetStreams(Guid itemId, Func<IReadOnlyList<MediaStream>> loader)
    {
        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(itemId, out var cached) && cached.ExpiresUtc > now)
        {
            return cached.Streams;
        }

        CleanupIfNeeded(now);

        var streams = loader();
        _cache[itemId] = new CachedStreams(streams, now.Add(EntryTtl));
        return streams;
    }

    public void Invalidate(Guid itemId)
    {
        _cache.TryRemove(itemId, out _);
    }

    private void CleanupIfNeeded(DateTime now)
    {
        if (_cache.Count < MaxCacheEntries)
        {
            return;
        }

        foreach (var kvp in _cache)
        {
            if (kvp.Value.ExpiresUtc <= now)
            {
                _cache.TryRemove(kvp.Key, out _);
            }
        }

        if (_cache.Count >= MaxCacheEntries)
        {
            var keysToRemove = _cache.Keys.Take(_cache.Count - (MaxCacheEntries / 2)).ToList();
            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    private sealed record CachedStreams(IReadOnlyList<MediaStream> Streams, DateTime ExpiresUtc);
}
