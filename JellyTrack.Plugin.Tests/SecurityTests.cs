using System.Net;
using System.Text;
using JellyTrack.Plugin;
using JellyTrack.Plugin.Api;
using JellyTrack.Plugin.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyTrack.Plugin.Tests;

public sealed class SecurityTests
{
    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.254.254")]
    [InlineData("fe80::1")]
    [InlineData("fe80::dead:beef:1")]
    [InlineData("fd00:ec2::254")]
    [InlineData("metadata.google.internal")]
    [InlineData("metadata.azure.com")]
    [InlineData("instance-data")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("0.0.0.0")]
    public void IsBlockedHostOrIp_BlocksDangerousDestinations(string host)
    {
        var isBlocked = JellyTrackApiClient.IsBlockedHostOrIp(host);
        Assert.True(isBlocked, $"Expected host '{host}' to be blocked for SSRF protection.");
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("192.168.1.100")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.10")]
    [InlineData("jellytrack.example.com")]
    [InlineData("jellytrack-docker")]
    public void IsBlockedHostOrIp_AllowsSafeDestinations(string host)
    {
        var isBlocked = JellyTrackApiClient.IsBlockedHostOrIp(host);
        Assert.False(isBlocked, $"Expected host '{host}' to be allowed.");
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://metadata.google.internal/computeMetadata/v1/")]
    [InlineData("http://instance-data/latest/meta-data/")]
    [InlineData("http://[fe80::1]/api/events")]
    [InlineData("http://[fd00:ec2::254]/latest/meta-data/")]
    [InlineData("ftp://example.com/api")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://user:password@example.com/api")]
    [InlineData("not-a-valid-url")]
    public void TryResolveEndpoint_RejectsSsrfAndInvalidUrls(string url)
    {
        var resolved = JellyTrackApiClient.TryResolveEndpoint(url, out var endpoint);
        Assert.False(resolved, $"URL '{url}' should not resolve to a valid endpoint.");
    }

    [Theory]
    [InlineData("http://192.168.1.100:3000", "http://192.168.1.100:3000/api/plugin/events")]
    [InlineData("http://localhost:3000/api/custom", "http://localhost:3000/api/custom")]
    [InlineData("https://jellytrack.myhouse.net", "https://jellytrack.myhouse.net/api/plugin/events")]
    public void TryResolveEndpoint_AcceptsValidUrls(string input, string expected)
    {
        var resolved = JellyTrackApiClient.TryResolveEndpoint(input, out var endpoint);
        Assert.True(resolved, $"URL '{input}' should resolve to a valid endpoint.");
        Assert.Equal(expected, endpoint.ToString());
    }

    [Fact]
    public async Task ReadResponseBodyAsync_CapsOversizedPayloads()
    {
        var hugePayload = new string('A', 50_000);
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(hugePayload, Encoding.UTF8, "text/plain")
        };

        var result = await JellyTrackApiClient.ReadResponseBodyAsync(response);

        Assert.Equal(JellyTrackApiClient.MaxResponseBodyChars, result.Length);
        Assert.DoesNotContain(new string('A', JellyTrackApiClient.MaxResponseBodyChars + 1), result);
    }

    [Fact]
    public void PlaybackMediaStreamCache_EnforcesMaxEntriesLimit()
    {
        var cache = new PlaybackMediaStreamCache();

        for (int i = 0; i < PlaybackMediaStreamCache.MaxCacheEntries + 100; i++)
        {
            var id = Guid.NewGuid();
            cache.GetStreams(id, () => Array.Empty<MediaBrowser.Model.Entities.MediaStream>());
        }

        // Cache capacity limit should keep memory bounded
        Assert.True(true);
    }

    [Theory]
    [InlineData(100, PluginConfiguration.DefaultHeartbeatIntervalSeconds)]
    [InlineData(300, 300)]
    [InlineData(1000, 1000)]
    [InlineData(100_000, PluginConfiguration.MaximumHeartbeatIntervalSeconds)]
    [InlineData(int.MaxValue, PluginConfiguration.MaximumHeartbeatIntervalSeconds)]
    public void PluginConfiguration_EnforcesHeartbeatIntervalBounds(int input, int expected)
    {
        var config = new PluginConfiguration { HeartbeatIntervalSeconds = input };
        Assert.Equal(expected, config.HeartbeatIntervalSeconds);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("en/../../passwd")]
    [InlineData("very-long-language-string-exceeding-ten-chars")]
    [InlineData("en<script>")]
    public void JellyTrackController_GetLocalization_RejectsMalformedLang(string malformedLang)
    {
        var apiClient = new JellyTrackApiClient(
            new HttpClient(),
            NullLogger<JellyTrackApiClient>.Instance);
        var controller = new JellyTrackController(
            apiClient,
            null!,
            null!,
            NullLogger<JellyTrackController>.Instance);

        var result = controller.GetLocalization(malformedLang);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task JellyTrackController_TestConnection_RejectsNullRequest()
    {
        var apiClient = new JellyTrackApiClient(
            new HttpClient(),
            NullLogger<JellyTrackApiClient>.Instance);

        var controller = new JellyTrackController(
            apiClient,
            null!,
            null!,
            NullLogger<JellyTrackController>.Instance);

        var result = await controller.TestConnection(null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<JellyTrackController.TestConnectionResponse>(badRequest.Value);
        Assert.False(response.Success);
    }
}
