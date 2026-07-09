using JellyTrack.Plugin;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JellyTrack.Plugin.Tests;

public sealed class PluginServiceRegistratorTests
{
    [Fact]
    public void RegisterServices_RegistersAllExpectedDependencies()
    {
        // Arrange
        var services = new ServiceCollection();
        var registrator = new PluginServiceRegistrator();
        IServerApplicationHost dummyHost = null!;

        // Act
        registrator.RegisterServices(services, dummyHost);

        // Assert
        // We verify that the registrator registers our services.
        // Let's assert a few key services are registered:
        var registeredTypes = services.Select(descriptor => descriptor.ServiceType).ToList();

        Assert.Contains(typeof(JellyTrack.Plugin.Services.JellyTrackApiClient), registeredTypes);
        Assert.Contains(typeof(JellyTrack.Plugin.Services.HeartbeatService), registeredTypes);
        Assert.Contains(typeof(JellyTrack.Plugin.Services.PlaybackSessionTelemetryState), registeredTypes);
    }
}
