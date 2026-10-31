using System;
using System.Collections.Generic;
using JellyTrack.Plugin.Services;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
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

    [Fact]
    public void ResolvesUserFromAdditionalUsersWhenSessionUserIdIsEmpty()
    {
        var expectedGuid = Guid.NewGuid();
        var session = new SessionInfo(null, null)
        {
            UserId = Guid.Empty,
            DeviceName = "Living Room TV",
            AdditionalUsers = new List<SessionUserInfo>
            {
                new SessionUserInfo
                {
                    UserId = expectedGuid,
                    UserName = "Bob"
                }
            }
        };

        var (userId, username) = UserSnapshotResolver.ResolveUserFromSession(session);

        Assert.Equal(expectedGuid.ToString("D"), userId);
        Assert.Equal("Bob", username);
    }

    [Fact]
    public void ResolvesUserFromUsersListWhenSessionUserIdIsEmpty()
    {
        var expectedGuid = Guid.NewGuid();
        var session = new SessionInfo(null, null)
        {
            UserId = Guid.Empty,
            DeviceName = "Living Room TV"
        };

        var dummyUser = new DummyUser { Id = expectedGuid, Username = "Charlie" };

        var (userId, username) = UserSnapshotResolver.ResolveUserFromSession(session, new[] { dummyUser });

        Assert.Equal(expectedGuid.ToString("D"), userId);
        Assert.Equal("Charlie", username);
    }

    private sealed class DummyUser
    {
        public Guid Id { get; set; }
        public string? Username { get; set; }
    }
}
