using System;
using JellyTrack.Plugin.Services;
using MediaBrowser.Controller.Session;
using Xunit;

namespace JellyTrack.Plugin.Tests;

public sealed class UserSnapshotResolverTests
{
    [Fact]
    public void ResolvesNullWhenSessionIsNull()
    {
        var (userId, username) = UserSnapshotResolver.ResolveUserFromSession(null);

        Assert.Null(userId);
        Assert.Null(username);
    }

    [Fact]
    public void ResolvesUserFromValidSession()
    {
        var expectedGuid = Guid.NewGuid();
        var session = new SessionInfo(null, null)
        {
            UserId = expectedGuid,
            UserName = "Alice"
        };

        var (userId, username) = UserSnapshotResolver.ResolveUserFromSession(session);

        Assert.Equal(expectedGuid.ToString("D"), userId);
        Assert.Equal("Alice", username);
    }

    [Fact]
    public void ResolvesNullUserIdWhenSessionUserIdIsEmpty()
    {
        var session = new SessionInfo(null, null)
        {
            UserId = Guid.Empty,
            DeviceName = "Living Room TV"
        };

        var (userId, username) = UserSnapshotResolver.ResolveUserFromSession(session);

        Assert.Null(userId);
        Assert.Equal("Living Room TV", username);
    }
}
