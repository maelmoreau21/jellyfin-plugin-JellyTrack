using System.Collections.Concurrent;
using MediaBrowser.Model.Entities;

namespace JellyTrack.Plugin.Services;

public sealed class PlaybackMediaStreamCache
{
    private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<Guid, CachedStreams> _cache = new();

    public IReadOnlyList<MediaStream> GetStreams(Guid itemId, Func<IReadOnlyList<MediaStream>> loader)
    {
        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(itemId, out var cached) && cached.ExpiresUtc > now)
        {
            return cached.Streams;
        }

        var streams = loader();
        _cache[itemId] = new CachedStreams(streams, now.Add(EntryTtl));
        return streams;
    }

    public void Invalidate(Guid itemId)
    {
        _cache.TryRemove(itemId, out _);
    }

    private sealed record CachedStreams(IReadOnlyList<MediaStream> Streams, DateTime ExpiresUtc);
}
