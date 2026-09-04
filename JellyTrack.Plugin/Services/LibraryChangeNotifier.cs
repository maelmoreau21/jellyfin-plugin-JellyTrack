using System.Collections.Concurrent;
using JellyTrack.Plugin.Models;
using JellyTrack.Plugin.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyTrack.Plugin.Services;

public class LibraryChangeNotifier : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly JellyTrackApiClient _apiClient;
    private readonly ILogger<LibraryChangeNotifier> _logger;

    private readonly ConcurrentDictionary<Guid, BaseItem> _pendingItems = new();
    private Timer? _debounceTimer;
    private readonly object _timerLock = new();
    private const int DebounceSeconds = 30;
    private const int MaxPendingItems = 1000;

    public LibraryChangeNotifier(
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        JellyTrackApiClient apiClient,
        ILogger<LibraryChangeNotifier> logger)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _apiClient = apiClient;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
        _logger.LogInformation("JellyTrack LibraryChangeNotifier started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
        _debounceTimer?.Dispose();
        _logger.LogInformation("JellyTrack LibraryChangeNotifier stopped");
        return Task.CompletedTask;
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.Enabled)
        {
            return;
        }

        var item = e.Item;

        // Only track actual media items (movies, episodes, audio)
        if (item is not (Video or Episode or Audio or MediaBrowser.Controller.Entities.Movies.Movie))
        {
            return;
        }

        _pendingItems.TryAdd(item.Id, item);

        if (_pendingItems.Count >= MaxPendingItems)
        {
            lock (_timerLock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }

            _ = FlushPendingItemsAsync();
            return;
        }

        // Reset the debounce timer
        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(
                _ => _ = FlushPendingItemsAsync(),
                null,
                TimeSpan.FromSeconds(DebounceSeconds),
                Timeout.InfiniteTimeSpan);
        }
    }

    private async Task FlushPendingItemsAsync()
    {
        if (_pendingItems.IsEmpty)
        {
            return;
        }

        // Drain all pending items
        var items = new List<BaseItem>();
        foreach (var key in _pendingItems.Keys.ToList())
        {
            if (_pendingItems.TryRemove(key, out var item))
            {
                items.Add(item);
            }
        }

        if (items.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Sending LibraryChanged event with {Count} items", items.Count);

        var libraryItems = items.Select(BuildLibraryItem).ToList();

        var payload = new LibraryChangedEvent
        {
            Timestamp = DateTime.UtcNow,
            Items = libraryItems
        };

        await _apiClient.SendEventAsync(payload).ConfigureAwait(false);
    }

    private LibraryItem BuildLibraryItem(BaseItem item)
    {
        var libraryItem = new LibraryItem
        {
            JellyfinMediaId = item.Id.ToString(),
            Title = item.Name,
            Type = item.GetBaseItemKind().ToString(),
            Genres = item.Genres?.ToList() ?? new List<string>(),
            DurationMs = item.RunTimeTicks.HasValue ? item.RunTimeTicks.Value / 10000 : 0
        };

        // Music info
        if (item is Audio audio)
        {
            libraryItem.Artist = audio.Artists?.FirstOrDefault();
        }

        // Resolution from video stream
        var streams = _mediaSourceManager.GetMediaStreams(item.Id);
        var videoStream = streams?.FirstOrDefault(s => s.Type == MediaStreamType.Video);
        if (videoStream is not null)
        {
            int width = videoStream.Width ?? 0;
            libraryItem.Resolution = width switch
            {
                >= 3800 => "4K",
                >= 1900 => "1080p",
                >= 1200 => "720p",
                _ => "SD"
            };
        }

        // Collection / library info
        try
        {
            var collectionFolders = _libraryManager.GetCollectionFolders(item);
            var folder = collectionFolders.FirstOrDefault();
            if (folder is not null)
            {
                libraryItem.LibraryName = folder.Name;
                if (folder is CollectionFolder cf)
                {
                    libraryItem.CollectionType = cf.CollectionType?.ToString()?.ToLowerInvariant()
                                                 ?? InferCollectionType(item);
                }
                else
                {
                    libraryItem.CollectionType = InferCollectionType(item);
                }
            }
            else
            {
                libraryItem.CollectionType = InferCollectionType(item);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not determine collection folder for item {ItemId}", item.Id);
            libraryItem.CollectionType = InferCollectionType(item);
        }

        // Parent ID
        if (item is Episode episode)
        {
            libraryItem.ParentId = episode.SeasonId.ToString();
        }
        else
        {
            var parent = item.GetParent();
            libraryItem.ParentId = parent?.Id.ToString();
        }

        return libraryItem;
    }

    private static string InferCollectionType(BaseItem item)
    {
        return item switch
        {
            Episode => "tvshows",
            Audio => "music",
            _ => "movies"
        };
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
    }
}
