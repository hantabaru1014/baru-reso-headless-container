using FrooxEngine;
using SkyFrost.Base;

namespace Headless.Extensions;

public static class WorldExtensions
{
    public static async Task<WorldStartupParameters> SetParametersAsync(this World world, WorldStartupParameters startupParameters, ILogger logger)
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

        world.SetCloudVariableParameters(startupParameters, logger);
        world.ConfigureParentSessions(startupParameters, logger);

        await world.ConfigurePermissionsAsync(startupParameters, logger);
        await world.SendAutomaticInvitesAsync(startupParameters, logger);

        startupParameters = await world.ConfigureSaveAsAsync(startupParameters, logger);

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

    public static async Task SendAutomaticInvitesAsync
    (
        this World world,
        WorldStartupParameters startupParameters,
        ILogger logger
    )
    {
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

    public static async Task<WorldStartupParameters> ConfigureSaveAsAsync
    (
        this World world,
        WorldStartupParameters startupParameters,
        ILogger logger
    )
    {
        string ownerID;
        switch (startupParameters.SaveAsOwner)
        {
            case SaveAsOwner.LocalMachine:
            {
                ownerID = $"M-{world.Engine.LocalDB.MachineID}";
                break;
            }
            case SaveAsOwner.CloudUser:
            {
                if (world.Engine.Cloud.CurrentUser is null)
                {
                    logger.LogWarning("World is set to be saved under cloud user, but not user is logged in");
                    return startupParameters;
                }

                ownerID = world.Engine.Cloud.CurrentUser.Id;
                break;
            }
            case null:
            {
                return startupParameters;
            }
            default:
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        var record = world.CorrespondingRecord;
        var originalOwnerId = record?.RecordId;
        if (record is null)
        {
            record = world.CreateNewRecord(ownerID);
            world.CorrespondingRecord = record;
        }
        else
        {
            record.OwnerId = ownerID;
            record.RecordId = RecordHelper.GenerateRecordID();
        }

        var transferer = new RecordOwnerTransferer(world.Engine, record.OwnerId, originalOwnerId);
        logger.LogInformation("Saving world under {SaveAs}", startupParameters.SaveAsOwner);

        var savedRecord = await Userspace.SaveWorld(world, record, transferer);
        logger.LogInformation("Saved successfully");

        startupParameters.SaveAsOwner = null;
        startupParameters.LoadWorldURL = savedRecord.GetUrl(world.Engine.Cloud.Platform).ToString();

        return startupParameters;
    }
}
