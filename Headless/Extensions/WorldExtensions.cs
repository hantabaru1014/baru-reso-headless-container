using FrooxEngine;
using Headless.Models;
using SkyFrost.Base;

namespace Headless.Extensions;

public static class WorldExtensions
{
    public static async Task<ExtendedWorldStartupParameters> SetParametersAsync(this World world, ExtendedWorldStartupParameters startupParameters, ILogger logger)
    {
        if (startupParameters.SessionName is not null)
        {
            world.Name = startupParameters.SessionName;
        }
        if (startupParameters.Tags is not null)
        {
            world.Tags = startupParameters.Tags;
        }
        world.AccessLevel = startupParameters.AccessLevel;
        world.UseCustomJoinVerifier = startupParameters.UseCustomJoinVerifier;
        world.HideFromListing = startupParameters.HideFromPublicListing is true;
        world.MaxUsers = startupParameters.MaxUsers;
        world.MobileFriendly = startupParameters.MobileFriendly;
        world.Description = startupParameters.Description;
        world.ForceFullUpdateCycle = !startupParameters.AutoSleep;
        world.SaveOnExit = startupParameters.SaveOnExit;

        var correspondingWorldId = startupParameters.OverrideCorrespondingWorldId;
        if (correspondingWorldId is not null && correspondingWorldId.IsValid)
        {
            world.CorrespondingWorldId = correspondingWorldId.ToString();
        }

        if (startupParameters.AwayKickMinutes > 0.0)
        {
            world.AwayKickEnabled = true;
            world.AwayKickMinutes = (float)startupParameters.AwayKickMinutes;
        }
        else
        {
            world.AwayKickEnabled = false;
            world.AwayKickMinutes = -1;
        }

        world.SetCloudVariableParameters(startupParameters, logger);
        world.ConfigureParentSessions(startupParameters, logger);
        if (startupParameters.InviteRequestHandlerUsernames != null && startupParameters.InviteRequestHandlerUsernames.Count() > 0)
        {
            world.ConfigureInviteRequestHandlerUsernames(startupParameters.InviteRequestHandlerUsernames, logger);
        }

        await world.ConfigurePermissionsAsync(startupParameters, logger);
        await world.SendAutomaticInvitesAsync(startupParameters, logger);

        return startupParameters;
    }

    public static void ConfigureParentSessions(this World world, WorldStartupParameters startupParameters, ILogger logger)
    {
        if (startupParameters.ParentSessionIds is null)
        {
            return;
        }

        var sessionIDs = new List<string>();
        foreach (var parentSessionID in startupParameters.ParentSessionIds)
        {
            if (!SessionInfo.IsValidSessionId(parentSessionID))
            {
                logger.LogWarning("Parent session ID {ID} is invalid", parentSessionID);
                continue;
            }

            logger.LogInformation("Parent session ID: {ID}", parentSessionID);
            sessionIDs.Add(parentSessionID);
        }

        world.ParentSessionIds = sessionIDs;
    }

    public static void ConfigureInviteRequestHandlerUsernames(this World world, IEnumerable<string> usernames, ILogger logger)
    {
        if (world.Engine.Cloud.CurrentUser is null)
        {
            logger.LogWarning("Not logged in, cannot forward invite requests!");
            return;
        }

        world.RunSynchronously(() =>
        {
            var currentUsers = world.GetInviteHandlerUsers();
            foreach (var username in usernames)
            {
                if (currentUsers.Contains(username)) continue;

                var contact = world.Engine.Cloud.Contacts.FindContact((c) => c.ContactUsername.Equals(username, StringComparison.InvariantCultureIgnoreCase));
                if (contact is null)
                {
                    logger.LogWarning(username + " is not in the contacts list, cannot setup as invite request handler");
                }
                else
                {
                    world.AddInviteRequestHandler(contact.ContactUserId);
                    logger.LogInformation("{0} ({1}) added as invite handler.", username, contact.ContactUserId);
                }
            }
        }, true);
    }

    public static async Task SendAutomaticInvitesAsync
    (
        this World world,
        ExtendedWorldStartupParameters startupParameters,
        ILogger logger
    )
    {
        foreach (var userId in startupParameters.JoinAllowedUserIds)
        {
            world.AllowUserToJoin(userId);
            logger.LogInformation("Allow join to {id}", userId);
        }

        if (startupParameters.AutoInviteUsernames is null)
        {
            return;
        }

        if (world.Engine.Cloud.CurrentUser is null)
        {
            logger.LogWarning("Not logged in, cannot send auto-invites");
            return;
        }

        foreach (var username in startupParameters.AutoInviteUsernames)
        {
            var contact = world.Engine.Cloud.Contacts.FindContact
            (
                f => f.ContactUsername.Equals(username, StringComparison.InvariantCultureIgnoreCase)
            );

            if (contact is null)
            {
                logger.LogWarning("{Username} is not in the friends list, cannot auto-invite", username);
                continue;
            }

            var messages = world.Engine.Cloud.Messages.GetUserMessages(contact.ContactUserId);
            if (startupParameters.AutoInviteMessage is not null)
            {
                if (!await messages.SendTextMessage(startupParameters.AutoInviteMessage))
                {
                    logger.LogWarning("Failed to send custom auto-invite message");
                }
            }

            world.AllowUserToJoin(contact.ContactUserId);
            var inviteMessage = await messages.CreateInviteMessage(world);
            if (!await messages.SendMessage(inviteMessage))
            {
                logger.LogWarning("Failed to send auto-invite");
            }
            else
            {
                logger.LogInformation("{Username} invited", username);
            }
        }
    }

    public static Task ConfigurePermissionsAsync
    (
        this World world,
        WorldStartupParameters startupParameters,
        ILogger logger
    )
    {
        return world.Coroutines.StartTask
        (
            static async args =>
            {
                await default(NextUpdate);
                if (!args.Startup.KeepOriginalRoles)
                {
                    args.World.Permissions.DefaultUserPermissions.Clear();
                }

                if (args.Startup.DefaultUserRoles is null)
                {
                    return;
                }

                foreach (var (user, role) in args.Startup.DefaultUserRoles)
                {
                    var userByName = await args.World.Engine.Cloud.Users.GetUserByName(user);
                    if (userByName.IsError)
                    {
                        args.Log.LogWarning("User {User} not found: {Reason}", user, userByName.State);
                        continue;
                    }

                    var roleByName = args.World.Permissions.FindRoleByName(role);
                    if (roleByName is null)
                    {
                        args.Log.LogWarning("Role {Role} not available for world {World}", role, args.World.RawName);
                        continue;
                    }

                    var permissionSet = args.World.Permissions.FilterRole(roleByName);
                    if (permissionSet != roleByName)
                    {
                        args.Log.LogWarning
                        (
                            "Cannot use default role {DefaultRole} for {Role} because it's higher than the host role {HostRole} in world {World}",
                            roleByName.RoleName.Value,
                            role,
                            permissionSet.RoleName.Value,
                            args.World.RawName
                        );
                    }

                    args.World.Permissions.DefaultUserPermissions.Remove(userByName.Entity.Id);
                    args.World.Permissions.DefaultUserPermissions.Add(userByName.Entity.Id, permissionSet);
                }
            },
            (World: world, Startup: startupParameters, Log: logger)
        );
    }

    public static void SetCloudVariableParameters
    (
        this World world,
        WorldStartupParameters startupParameters,
        ILogger logger
    )
    {
        if (startupParameters.RoleCloudVariable is not null)
        {
            if (!CloudVariableHelper.IsValidPath(startupParameters.RoleCloudVariable))
            {
                logger.LogWarning("Invalid RoleCloudVariable: {Variable}", startupParameters.RoleCloudVariable);
            }
            else
            {
                world.Permissions.DefaultRoleCloudVariable = startupParameters.RoleCloudVariable;
            }
        }

        if (startupParameters.AllowUserCloudVariable is not null)
        {
            if (!CloudVariableHelper.IsValidPath(startupParameters.AllowUserCloudVariable))
            {
                logger.LogWarning("Invalid AllowUserCloudVariable: {Variable}", startupParameters.AllowUserCloudVariable);
            }
            else
            {
                world.AllowUserCloudVariable = startupParameters.AllowUserCloudVariable;
            }
        }

        if (startupParameters.DenyUserCloudVariable is not null)
        {
            if (!CloudVariableHelper.IsValidPath(startupParameters.DenyUserCloudVariable))
            {
                logger.LogWarning("Invalid DenyUserCloudVariable: {Variable}", startupParameters.DenyUserCloudVariable);
            }
            else
            {
                world.DenyUserCloudVariable = startupParameters.DenyUserCloudVariable;
            }
        }

        if (startupParameters.RequiredUserJoinCloudVariable is not null)
        {
            if (!CloudVariableHelper.IsValidPath(startupParameters.RequiredUserJoinCloudVariable))
            {
                logger.LogWarning
                    ("Invalid RequiredUserJoinCloudVariable: {Variable}", startupParameters.RequiredUserJoinCloudVariable);
            }
            else
            {
                world.RequiredUserJoinCloudVariable = startupParameters.RequiredUserJoinCloudVariable;
            }
        }

        if (startupParameters.RequiredUserJoinCloudVariableDenyMessage is not null)
        {
            world.RequiredUserJoinCloudVariableDenyMessage = startupParameters.RequiredUserJoinCloudVariableDenyMessage;
        }
    }
}
