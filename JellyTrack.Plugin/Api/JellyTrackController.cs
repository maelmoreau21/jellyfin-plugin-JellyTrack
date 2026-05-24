using System.Net;
using JellyTrack.Plugin.Models;
using JellyTrack.Plugin.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;

namespace JellyTrack.Plugin.Api;

[ApiController]
[Route("JellyTrack")]
[Authorize]
public class JellyTrackController : ControllerBase
{
    private readonly JellyTrackApiClient _apiClient;
    private readonly IServerApplicationHost _applicationHost;
    private readonly IUserManager _userManager;
    private readonly ILogger<JellyTrackController> _logger;

    public JellyTrackController(
        JellyTrackApiClient apiClient,
        IServerApplicationHost applicationHost,
        IUserManager userManager,
        ILogger<JellyTrackController> logger)
    {
        _apiClient = apiClient;
        _applicationHost = applicationHost;
        _userManager = userManager;
        _logger = logger;
    }



    [HttpGet("Localization/{lang}")]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    [ProducesResponseType((int)HttpStatusCode.NotFound)]
    public ActionResult GetLocalization(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) lang = "en";

        var assembly = typeof(JellyTrackController).Assembly;
        var tried = new[]
        {
            $"{typeof(Plugin).Namespace}.Localization.{lang}.json",
            $"{typeof(Plugin).Namespace}.Localization.{(lang.Contains('-') ? lang.Split('-')[0] : lang)}.json",
            $"{typeof(Plugin).Namespace}.Localization.en.json"
        };

        foreach (var name in tried)
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return Content(json, "application/json");
        }

        return NotFound();
    }

    [HttpPost("Test")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    [ProducesResponseType((int)HttpStatusCode.BadRequest)]
    [ProducesResponseType((int)HttpStatusCode.ServiceUnavailable)]
    [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
    public async Task<ActionResult> TestConnection([FromBody] TestRequest request, CancellationToken cancellationToken)
    {

        if (string.IsNullOrWhiteSpace(request.Url) || string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return BadRequest(new TestConnectionResponse
            {
                Success = false,
                Message = "URL and API Key are required."
            });
        }

        var testEvent = new HeartbeatEvent
        {
            PluginVersion = Plugin.Instance?.Version.ToString() ?? "0.0.0.0",
            ServerName = _applicationHost.FriendlyName,
            JellyfinVersion = _applicationHost.ApplicationVersionString,
            Users = new List<HeartbeatUser>(),
            PluginMetrics = BuildHeartbeatMetrics()
        };

        try 
        {
            var result = await _apiClient.TestConnectionAsync(request.Url, request.ApiKey, testEvent, cancellationToken);
            var response = new TestConnectionResponse
            {
                Success = result.Success,
                Endpoint = result.Endpoint,
                StatusCode = (int?)result.StatusCode,
                Message = result.Message
            };

            if (result.Success)
            {
                return Ok(response);
            }

            return StatusCode((int)(result.StatusCode ?? HttpStatusCode.ServiceUnavailable), response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing connection to JellyTrack");
            return StatusCode((int)HttpStatusCode.InternalServerError, new TestConnectionResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
    }

    [HttpPost("HeartbeatNow")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    [ProducesResponseType((int)HttpStatusCode.BadRequest)]
    [ProducesResponseType((int)HttpStatusCode.ServiceUnavailable)]
    public async Task<ActionResult> SendHeartbeatNow(CancellationToken cancellationToken)
    {

        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.Enabled)
        {
            return BadRequest(new TestConnectionResponse
            {
                Success = false,
                Message = "Plugin disabled or configuration unavailable."
            });
        }

        if (string.IsNullOrWhiteSpace(config.JellyTrackUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return BadRequest(new TestConnectionResponse
            {
                Success = false,
                Message = "JellyTrack URL and API key must be configured first."
            });
        }

        var payload = BuildHeartbeatPayload();
        var success = await _apiClient.SendEventAsync(payload, cancellationToken).ConfigureAwait(false);
        if (success)
        {
            return Ok(new TestConnectionResponse
            {
                Success = true,
                Message = "Heartbeat sent successfully."
            });
        }

        return StatusCode((int)HttpStatusCode.ServiceUnavailable, new TestConnectionResponse
        {
            Success = false,
            Message = "Heartbeat could not be delivered to JellyTrack. Check Jellyfin logs for details."
        });
    }

    private HeartbeatEvent BuildHeartbeatPayload()
    {
        return new HeartbeatEvent
        {
            PluginVersion = Plugin.Instance?.Version.ToString() ?? "0.0.0.0",
            ServerName = _applicationHost.FriendlyName,
            JellyfinVersion = _applicationHost.ApplicationVersionString,
            Users = UserSnapshotResolver.ResolveHeartbeatUsers(_userManager, _logger),
            PluginMetrics = BuildHeartbeatMetrics(),
        };
    }

    private HeartbeatPluginMetrics BuildHeartbeatMetrics()
    {
        var metrics = _apiClient.GetRuntimeMetricsSnapshot();
        return new HeartbeatPluginMetrics
        {
            QueueDepth = metrics.QueueDepth,
            Retries = metrics.RetryAttempts,
            LastHttpCode = metrics.LastHttpCode,
        };
    }

    public class TestRequest
    {
        public string Url { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }

    public class TestConnectionResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int? StatusCode { get; set; }
        public string Endpoint { get; set; } = string.Empty;
    }
}
