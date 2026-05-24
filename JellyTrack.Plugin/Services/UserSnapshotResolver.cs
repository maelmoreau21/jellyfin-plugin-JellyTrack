using System;
using System.Collections.Generic;
using JellyTrack.Plugin.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace JellyTrack.Plugin.Services;

internal static class UserSnapshotResolver
{
    public static List<HeartbeatUser> ResolveHeartbeatUsers(IUserManager userManager, ILogger logger)
    {
        var users = new List<HeartbeatUser>();

        foreach (var user in userManager.GetUsers())
        {
            var userId = user.Id.ToString("D");
            if (string.IsNullOrWhiteSpace(userId))
            {
                logger.LogWarning("Heartbeat: User found but ID is null/empty. Skipping.");
                continue;
            }

            var username = user.Username ?? "Unknown";

            users.Add(new HeartbeatUser
            {
                JellyfinUserId = userId,
                Username = username,
            });
        }

        return users;
    }

    public static (string? JellyfinUserId, string? Username) ResolveUserFromSession(SessionInfo? session)
    {
        if (session is null)
        {
            return (null, null);
        }

        var userId = session.UserId == Guid.Empty ? null : session.UserId.ToString("D");
        var username = session.UserName ?? session.DeviceName;

        return (userId, username);
    }
}