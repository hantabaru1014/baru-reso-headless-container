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
            DefaultUserRoles = { parameters.DefaultUserRoles?.Select(p => new Rpc.DefaultUserRole { UserName = p.Key, Role = p.Value }) },
            AwayKickMinutes = (float)parameters.AwayKickMinutes,
            IdleRestartIntervalSeconds = (int)parameters.IdleRestartInterval,
            SaveOnExit = parameters.SaveOnExit,
            AutoSaveIntervalSeconds = (int)parameters.AutoSaveInterval,
            AutoSleep = parameters.AutoSleep,
            ForcePort = parameters.ForcePort ?? 0,
            ParentSessionIds = { parameters.ParentSessionIds ?? [] },
            AutoRecover = parameters.AutoRecover,
            ForcedRestartIntervalSeconds = (int)parameters.ForcedRestartInterval,
            InviteRequestHandlerUsernames = { parameters.InviteRequestHandlerUsernames ?? [] }
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
        return result;
    }

    public static SkyFrost.Base.WorldStartupParameters ToResonite(this Rpc.WorldStartupParameters parameters)
    {
        var result = new SkyFrost.Base.WorldStartupParameters
        {
            IsEnabled = true,
            MaxUsers = parameters.MaxUsers,
            AccessLevel = parameters.AccessLevel.ToResonite(),
            AutoInviteUsernames = parameters.AutoInviteUsernames.ToList(),
            Tags = parameters.Tags.ToList(),
            HideFromPublicListing = parameters.HideFromPublicListing,
            DefaultUserRoles = parameters.DefaultUserRoles.ToDictionary(r => r.UserName, r => r.Role),
            AwayKickMinutes = parameters.AwayKickMinutes,
            IdleRestartInterval = parameters.IdleRestartIntervalSeconds,
            SaveOnExit = parameters.SaveOnExit,
            AutoSaveInterval = parameters.AutoSaveIntervalSeconds,
            AutoSleep = parameters.AutoSleep,
            ForcePort = parameters.ForcePort > 0 ? (ushort)parameters.ForcePort : null,
            ParentSessionIds = parameters.ParentSessionIds.ToList(),
            AutoRecover = parameters.AutoRecover,
            ForcedRestartInterval = parameters.ForcedRestartIntervalSeconds,
            InviteRequestHandlerUsernames = parameters.InviteRequestHandlerUsernames.ToList()
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

        return result;
    }

    public static Rpc.Session ToProto(this Models.RunningSession session)
    {
        var info = session.WorldInstance.GenerateSessionInfo();
        var result = new Rpc.Session
        {
            Id = info.SessionId,
            Name = info.Name ?? "<Empty Name>",
            Description = info.Description ?? "",
            Tags = { info.Tags ?? [] },
            AccessLevel = info.AccessLevel.ToProto(),
            StartupParameters = session.StartInfo.ToProto(),
            UsersCount = info.JoinedUsers,
            MaxUsers = info.MaximumUsers,
            SessionUrl = CloudUtils.MakeSessionGoURL(info.SessionId),
            TimeRunningMs = (int)Math.Round(session.TimeRunning.TotalMilliseconds),
            StartedAt = Timestamp.FromDateTime(info.SessionBeginTime),
            AwayKickMinutes = info.AwayKickEnabled ? info.AwayKickMinutes : -1,
            IdleRestartIntervalSeconds = (int)session.IdleRestartInterval.TotalSeconds,
            SaveOnExit = session.WorldInstance.SaveOnExit,
            AutoSaveIntervalSeconds = (int)session.AutosaveInterval.TotalSeconds,
            HideFromPublicListing = info.HideFromListing,
            LastSavedAt = Timestamp.FromDateTimeOffset(session.LastSaveTime),
            CanSave = Userspace.CanSave(session.WorldInstance),
        };
        if (session.WorldInstance.RecordURL != null)
        {
            result.WorldUrl = session.WorldInstance.RecordURL.ToString();
        }
        if (info.ThumbnailUrl is not null)
        {
            result.ThumbnailUrl = CloudUtils.ResolveURL(info.ThumbnailUrl);
        }
        return result;
    }
}