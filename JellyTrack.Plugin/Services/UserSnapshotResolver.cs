using System;
using System.Collections.Generic;
using JellyTrack.Plugin.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace JellyTrack.Plugin.Services;

public static class UserSnapshotResolver
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

    public static (string? JellyfinUserId, string? Username) ResolveUserFromSession(
        SessionInfo? session,
        IEnumerable<object>? users = null)
    {
        if (session is null)
        {
            if (users is not null)
            {
                foreach (var u in users)
                {
                    if (u is null) continue;
                    var idProp = u.GetType().GetProperty("Id");
                    var nameProp = u.GetType().GetProperty("Username");
                    if (idProp?.GetValue(u) is Guid g && g != Guid.Empty)
                    {
                        var name = nameProp?.GetValue(u) as string;
                        return (g.ToString("D"), name);
                    }
                }
            }
            return (null, null);
        }

        var userId = session.UserId;
        var username = session.UserName;

        if (userId == Guid.Empty && session.AdditionalUsers is { Count: > 0 })
        {
            foreach (var add in session.AdditionalUsers)
            {
                if (add.UserId != Guid.Empty)
                {
                    userId = add.UserId;
                    username ??= add.UserName;
                    break;
                }
            }
        }

        if (userId == Guid.Empty && users is not null)
        {
            foreach (var u in users)
            {
                if (u is null) continue;
                var idProp = u.GetType().GetProperty("Id");
                var nameProp = u.GetType().GetProperty("Username");
                if (idProp?.GetValue(u) is Guid g && g != Guid.Empty)
                {
                    userId = g;
                    username ??= nameProp?.GetValue(u) as string;
                    break;
                }
            }
        }

        if (userId == Guid.Empty)
        {
            return (null, username ?? session.DeviceName);
        }

        return (userId.ToString("D"), username ?? session.DeviceName ?? "Unknown");
    }
}
