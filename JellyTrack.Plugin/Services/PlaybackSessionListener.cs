using System;
using System.Threading;
using System.Threading.Tasks;
using JellyTrack.Plugin.Notifiers;
using MediaBrowser.Controller.Events.Session;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyTrack.Plugin.Services;

public sealed class PlaybackSessionListener : IHostedService, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly PlaybackStartNotifier _startNotifier;
    private readonly PlaybackProgressNotifier _progressNotifier;
    private readonly PlaybackStopNotifier _stopNotifier;
    private readonly SessionEndedNotifier _sessionEndedNotifier;
    private readonly ILogger<PlaybackSessionListener> _logger;

    public PlaybackSessionListener(
        ISessionManager sessionManager,
        PlaybackStartNotifier startNotifier,
        PlaybackProgressNotifier progressNotifier,
        PlaybackStopNotifier stopNotifier,
        SessionEndedNotifier sessionEndedNotifier,
        ILogger<PlaybackSessionListener> logger)
    {
        _sessionManager = sessionManager;
        _startNotifier = startNotifier;
        _progressNotifier = progressNotifier;
        _stopNotifier = stopNotifier;
        _sessionEndedNotifier = sessionEndedNotifier;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _sessionManager.SessionEnded += OnSessionEnded;
        _logger.LogInformation("JellyTrack PlaybackSessionListener subscribed to ISessionManager events");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _sessionManager.SessionEnded -= OnSessionEnded;
        _logger.LogInformation("JellyTrack PlaybackSessionListener unsubscribed from ISessionManager events");
        return Task.CompletedTask;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        var startArgs = new PlaybackStartEventArgs
        {
            Item = e.Item,
            Session = e.Session,
            PlaybackPositionTicks = e.PlaybackPositionTicks,
            Users = e.Users,
            MediaInfo = e.MediaInfo,
            MediaSourceId = e.MediaSourceId,
            IsPaused = e.IsPaused,
            IsAutomated = e.IsAutomated,
            DeviceId = e.DeviceId,
            DeviceName = e.DeviceName,
            ClientName = e.ClientName,
            PlaySessionId = e.PlaySessionId
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await _startNotifier.OnEvent(startArgs).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PlaybackStart from ISessionManager");
            }
        });
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _progressNotifier.OnEvent(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PlaybackProgress from ISessionManager");
            }
        });
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _stopNotifier.OnEvent(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PlaybackStopped from ISessionManager");
            }
        });
    }

    private void OnSessionEnded(object? sender, SessionEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (e.SessionInfo is not null)
                {
                    await _sessionEndedNotifier.OnEvent(new SessionEndedEventArgs(e.SessionInfo)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SessionEnded from ISessionManager");
            }
        });
    }

    public void Dispose()
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _sessionManager.SessionEnded -= OnSessionEnded;
    }
}
