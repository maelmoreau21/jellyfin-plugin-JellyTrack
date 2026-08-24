using JellyTrack.Plugin;
using Xunit;

namespace JellyTrack.Plugin.Tests;

public sealed class PluginConfigurationTests
{
    [Fact]
    public void DefaultValuesAreCorrect()
    {
        var config = new PluginConfiguration();

        Assert.False(config.Enabled);
        Assert.Equal(string.Empty, config.JellyTrackUrl);
        Assert.Equal(string.Empty, config.ApiKey);
        Assert.Equal(PluginConfiguration.DefaultHeartbeatIntervalSeconds, config.HeartbeatIntervalSeconds);
        Assert.Equal(PluginConfiguration.DefaultProgressIntervalSeconds, config.ProgressIntervalSeconds);
        Assert.Equal(PluginConfiguration.DefaultPausedProgressIntervalSeconds, config.PausedProgressIntervalSeconds);
        Assert.Equal(PluginConfiguration.DefaultSeekThresholdSeconds, config.SeekThresholdSeconds);
        Assert.Equal(PluginConfiguration.DefaultRetryQueueSize, config.RetryQueueSize);
        Assert.Equal(PluginConfiguration.DefaultRetryFlushBatchSize, config.RetryFlushBatchSize);
        Assert.True(config.TrackPauseResume);
        Assert.True(config.TrackSeek);
        Assert.True(config.TrackAudioSubtitleChanges);
        Assert.True(config.TrackSessionEnded);
        Assert.Equal(string.Empty, config.PreferredLanguage);
    }

    [Theory]
    [InlineData(100, PluginConfiguration.DefaultHeartbeatIntervalSeconds)]
    [InlineData(299, PluginConfiguration.DefaultHeartbeatIntervalSeconds)]
    [InlineData(300, 300)]
    [InlineData(900, 900)]
    public void NormalizesHeartbeatIntervalSeconds(int input, int expected)
    {
        var config = new PluginConfiguration { HeartbeatIntervalSeconds = input };
        Assert.Equal(expected, config.HeartbeatIntervalSeconds);
    }

    [Theory]
    [InlineData(0, PluginConfiguration.DefaultProgressIntervalSeconds)]
    [InlineData(1, 1)]
    [InlineData(15, 15)]
    [InlineData(4000, 3600)]
    public void NormalizesProgressIntervalSeconds(int input, int expected)
    {
        var config = new PluginConfiguration { ProgressIntervalSeconds = input };
        Assert.Equal(expected, config.ProgressIntervalSeconds);
    }

    [Theory]
    [InlineData(1, PluginConfiguration.DefaultPausedProgressIntervalSeconds)]
    [InlineData(5, 5)]
    [InlineData(30, 30)]
    [InlineData(5000, 3600)]
    public void NormalizesPausedProgressIntervalSeconds(int input, int expected)
    {
        var config = new PluginConfiguration { PausedProgressIntervalSeconds = input };
        Assert.Equal(expected, config.PausedProgressIntervalSeconds);
    }

    [Theory]
    [InlineData(2, PluginConfiguration.DefaultSeekThresholdSeconds)]
    [InlineData(5, 5)]
    [InlineData(20, 20)]
    [InlineData(500, 300)]
    public void NormalizesSeekThresholdSeconds(int input, int expected)
    {
        var config = new PluginConfiguration { SeekThresholdSeconds = input };
        Assert.Equal(expected, config.SeekThresholdSeconds);
    }
}
