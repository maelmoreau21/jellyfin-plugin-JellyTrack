using JellyTrack.Plugin.Services;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyTrack.Plugin.Tests;

public sealed class LibraryChangeNotifierTests
{
    [Fact]
    public void LibraryBatchDeduplicatesMediaAndIsBoundedUnderConcurrentUpdates()
    {
        using var notifier = new LibraryChangeNotifier(null!, null!, null!, NullLogger<LibraryChangeNotifier>.Instance);
        var item = new Movie { Id = Guid.NewGuid() };
        Assert.True(notifier.TryAddPendingItem(item));
        Assert.True(notifier.TryAddPendingItem(item));
        Assert.Equal(1, notifier.PendingItemCount);
        Parallel.For(0, 5000, _ => notifier.TryAddPendingItem(new Movie { Id = Guid.NewGuid() }));
        Assert.Equal(1000, notifier.PendingItemCount);
        Assert.False(notifier.TryAddPendingItem(new Movie { Id = Guid.NewGuid() }));
    }
}
