using FrooxEngine;
using Google.Protobuf.WellKnownTypes;
using Headless.Libs;
using Headless.Rpc;
using SkyFrost.Base;

namespace Headless.Extensions;

public static class RpcConversionExtensions
{
    public static AccessLevel ToProto(this SessionAccessLevel level)
    {
        return level switch
        {
            SessionAccessLevel.Private => AccessLevel.Private,
            SessionAccessLevel.LAN => AccessLevel.Lan,
            SessionAccessLevel.Contacts => AccessLevel.Contacts,
            SessionAccessLevel.ContactsPlus => AccessLevel.ContactsPlus,
            SessionAccessLevel.RegisteredUsers => AccessLevel.RegisteredUsers,
            SessionAccessLevel.Anyone => AccessLevel.Anyone,
            _ => AccessLevel.Unspecified
        };
    }

    public static SessionAccessLevel ToResonite(this AccessLevel level)
    {
        return level switch
        {
            AccessLevel.Private => SessionAccessLevel.Private,
            AccessLevel.Lan => SessionAccessLevel.LAN,
            AccessLevel.Contacts => SessionAccessLevel.Contacts,
            AccessLevel.ContactsPlus => SessionAccessLevel.ContactsPlus,
            AccessLevel.RegisteredUsers => SessionAccessLevel.RegisteredUsers,
            AccessLevel.Anyone => SessionAccessLevel.Anyone,
            _ => SessionAccessLevel.Private
        };
    }

    public static Rpc.WorldStartupParameters ToProto(this SkyFrost.Base.WorldStartupParameters parameters)
    {
        var result = new Rpc.WorldStartupParameters
        {
            MaxUsers = parameters.MaxUsers,
            AccessLevel = parameters.AccessLevel.ToProto(),
            AutoInviteUsernames = { parameters.AutoInviteUsernames ?? [] },
            Tags = { parameters.Tags ?? [] },
            HideFromPublicListing = parameters.HideFromPublicListing ?? false,
            DefaultUserRoles = { parameters.DefaultUserRoles?.Select(p => new Rpc.DefaultUserRole { UserName = p.Key, Role = p.Value }) ?? new List<DefaultUserRole>() },
            AwayKickMinutes = (float)parameters.AwayKickMinutes,
            IdleRestartIntervalSeconds = (int)parameters.IdleRestartInterval,
            SaveOnExit = parameters.SaveOnExit,
            AutoSaveIntervalSeconds = (int)parameters.AutoSaveInterval,
            AutoSleep = parameters.AutoSleep,
            ForcePort = parameters.ForcePort ?? 0,
            ParentSessionIds = { parameters.ParentSessionIds ?? [] },
            AutoRecover = parameters.AutoRecover,
            ForcedRestartIntervalSeconds = (int)parameters.ForcedRestartInterval,
            InviteRequestHandlerUsernames = { parameters.InviteRequestHandlerUsernames ?? [] },
            UseCustomJoinVerifier = parameters.UseCustomJoinVerifier,
            MobileFriendly = parameters.MobileFriendly,
            KeepOriginalRoles = parameters.KeepOriginalRoles
        };
        if (parameters.SessionName is not null)
        {
            result.Name = parameters.SessionName;
        }
        if (parameters.CustomSessionId is not null)
        {
            result.CustomSessionId = parameters.CustomSessionId;
        }
        if (parameters.Description is not null)
        {
            result.Description = parameters.Description;
        }
        if (parameters.LoadWorldURL is not null)
        {
            result.LoadWorldUrl = parameters.LoadWorldURL;
        }
        else
        {
            result.LoadWorldPresetName = parameters.LoadWorldPresetName;
        }

        if (parameters.OverrideCorrespondingWorldId != null)
        {
            result.OverrideCorrespondingWorldId = new Rpc.RecordId
            {
                Id = parameters.OverrideCorrespondingWorldId.Id,
                OwnerId = parameters.OverrideCorrespondingWorldId.OwnerId
            };
        }

        if (parameters.RoleCloudVariable != null)
        {
            result.RoleCloudVariable = parameters.RoleCloudVariable;
        }

        if (parameters.AllowUserCloudVariable != null)
        {
            result.AllowUserCloudVariable = parameters.AllowUserCloudVariable;
        }

        if (parameters.DenyUserCloudVariable != null)
        {
            result.DenyUserCloudVariable = parameters.DenyUserCloudVariable;
        }

        if (parameters.RequiredUserJoinCloudVariable != null)
        {
            result.RequiredUserJoinCloudVariable = parameters.RequiredUserJoinCloudVariable;
        }

        if (parameters.RequiredUserJoinCloudVariableDenyMessage != null)
        {
            result.RequiredUserJoinCloudVariableDenyMessage = parameters.RequiredUserJoinCloudVariableDenyMessage;
        }

        if (parameters.AutoInviteMessage != null)
        {
            result.AutoInviteMessage = parameters.AutoInviteMessage;
        }

        return result;
    }

    public static SkyFrost.Base.WorldStartupParameters ToResonite(this Rpc.WorldStartupParameters parameters)
    {
        var result = new SkyFrost.Base.WorldStartupParameters
        {
            IsEnabled = true,
            AccessLevel = parameters.AccessLevel.ToResonite(),
            AutoInviteUsernames = parameters.AutoInviteUsernames.ToList(),
            Tags = parameters.Tags.ToList(),
            HideFromPublicListing = parameters.HideFromPublicListing,
            DefaultUserRoles = parameters.DefaultUserRoles?.ToDictionary(r => r.UserName, r => r.Role) ?? new(),
            AwayKickMinutes = parameters.AwayKickMinutes == 0 ? -1 : parameters.AwayKickMinutes,
            IdleRestartInterval = parameters.IdleRestartIntervalSeconds == 0 ? -1 : parameters.IdleRestartIntervalSeconds,
            SaveOnExit = parameters.SaveOnExit,
            AutoSaveInterval = parameters.AutoSaveIntervalSeconds == 0 ? -1 : parameters.AutoSaveIntervalSeconds,
            AutoSleep = parameters.AutoSleep,
            ForcePort = parameters.ForcePort > 0 ? (ushort)parameters.ForcePort : null,
            ParentSessionIds = parameters.ParentSessionIds.ToList(),
            AutoRecover = parameters.AutoRecover,
            ForcedRestartInterval = parameters.ForcedRestartIntervalSeconds == 0 ? -1 : parameters.ForcedRestartIntervalSeconds,
            InviteRequestHandlerUsernames = parameters.InviteRequestHandlerUsernames.ToList(),
            UseCustomJoinVerifier = parameters.UseCustomJoinVerifier,
            MobileFriendly = parameters.MobileFriendly,
            KeepOriginalRoles = parameters.KeepOriginalRoles
        };

        if (!string.IsNullOrEmpty(parameters.Name))
        {
            result.SessionName = parameters.Name;
        }

        if (!string.IsNullOrEmpty(parameters.CustomSessionId))
        {
            result.CustomSessionId = parameters.CustomSessionId;
        }

        if (!string.IsNullOrEmpty(parameters.Description))
        {
            result.Description = parameters.Description;
        }

        if (!string.IsNullOrEmpty(parameters.LoadWorldUrl))
        {
            result.LoadWorldURL = parameters.LoadWorldUrl;
        }
        else if (!string.IsNullOrEmpty(parameters.LoadWorldPresetName))
        {
            result.LoadWorldPresetName = parameters.LoadWorldPresetName;
        }

        if (parameters.HasMaxUsers)
        {
            result.MaxUsers = parameters.MaxUsers;
        }

        if (parameters.OverrideCorrespondingWorldId != null)
        {
            result.OverrideCorrespondingWorldId = new SkyFrost.Base.RecordId
            {
                Id = parameters.OverrideCorrespondingWorldId.Id,
                OwnerId = parameters.OverrideCorrespondingWorldId.OwnerId
            };
        }

        if (!string.IsNullOrEmpty(parameters.RoleCloudVariable))
        {
            result.RoleCloudVariable = parameters.RoleCloudVariable;
        }

        if (!string.IsNullOrEmpty(parameters.AllowUserCloudVariable))
        {
            result.AllowUserCloudVariable = parameters.AllowUserCloudVariable;
        }

        if (!string.IsNullOrEmpty(parameters.DenyUserCloudVariable))
        {
            result.DenyUserCloudVariable = parameters.DenyUserCloudVariable;
        }

        if (!string.IsNullOrEmpty(parameters.RequiredUserJoinCloudVariable))
        {
            result.RequiredUserJoinCloudVariable = parameters.RequiredUserJoinCloudVariable;
        }

        if (!string.IsNullOrEmpty(parameters.RequiredUserJoinCloudVariableDenyMessage))
        {
            result.RequiredUserJoinCloudVariableDenyMessage = parameters.RequiredUserJoinCloudVariableDenyMessage;
        }

        if (!string.IsNullOrEmpty(parameters.AutoInviteMessage))
        {
            result.AutoInviteMessage = parameters.AutoInviteMessage;
        }

        return result;
    }

    public static Rpc.Session ToProto(this Models.RunningSession session)
    {
        var info = session.Instance.GenerateSessionInfo();
        var result = new Rpc.Session
        {
            Id = info.SessionId,
            Name = info.Name ?? "<Empty Name>",
            Description = info.Description ?? "",
            Tags = { info.Tags ?? [] },
            AccessLevel = info.AccessLevel.ToProto(),
            StartupParameters = session.GenerateStartupParameters().ToProto(),
            UsersCount = info.JoinedUsers,
            MaxUsers = info.MaximumUsers,
            SessionUrl = CloudUtils.MakeSessionGoURL(info.SessionId),
            StartedAt = Timestamp.FromDateTime(info.SessionBeginTime),
            AwayKickMinutes = info.AwayKickEnabled ? info.AwayKickMinutes : -1,
            IdleRestartIntervalSeconds = (int)session.IdleRestartInterval.TotalSeconds,
            SaveOnExit = session.Instance.SaveOnExit,
            AutoSaveIntervalSeconds = (int)session.AutosaveInterval.TotalSeconds,
            HideFromPublicListing = info.HideFromListing,
            CanSave = Userspace.ShouldSave(session.Instance),
        };
        if (session.Instance.RecordURL is not null)
        {
            result.WorldUrl = session.Instance.RecordURL.ToString();
        }
        if (info.ThumbnailUrl is not null)
        {
            result.ThumbnailUrl = CloudUtils.ResolveURL(info.ThumbnailUrl);
        }
        if (session.LastSavedAt is not null)
        {
            result.LastSavedAt = Timestamp.FromDateTimeOffset(session.LastSavedAt ?? throw new Exception("LastSavedAt is null"));
        }
        return result;
    }
}