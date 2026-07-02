using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Headless.Libs;
using Headless.Rpc;
using SkyFrost.Base;

namespace Headless.Services;

public partial class GrpcControllerService
{
    public override Task<GetAccountInfoResponse> GetAccountInfo(GetAccountInfoRequest request, ServerCallContext context)
    {
        var cloud = _engine.Cloud;
        if (cloud.CurrentUser is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Headless is not login"));
        }

        var storage = cloud.Storage.CurrentStorage;
        return Task.FromResult(new GetAccountInfoResponse
        {
            UserId = cloud.CurrentUserID,
            DisplayName = cloud.CurrentUsername,
            StorageQuotaBytes = storage.QuotaBytes,
            StorageUsedBytes = storage.UsedBytes,
        });
    }

    public override async Task<FetchWorldInfoResponse> FetchWorldInfo(FetchWorldInfoRequest request, ServerCallContext context)
    {
        var cloudResult = await _engine.RecordManager.FetchRecord(new Uri(request.Url));
        if (!cloudResult.IsOK)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed to fetch record"));
        }
        var record = cloudResult.Entity;
        var canModify = _engine.RecordManager.CanModify(record);

        return new FetchWorldInfoResponse
        {
            Name = record.Name ?? "Unnamed",
            Description = record.Description ?? "",
            ThumbnailUrl = CloudUtils.ResolveURL(record.ThumbnailURI) ?? "",
            DefaultMaxUsers = -1, // TODO
            OwnerId = record.OwnerId ?? "",
            IsPublic = record.IsPublic,
            CanModify = canModify,
            IsReadonly = record.IsReadOnly,
            Tags = { record.Tags ?? [] },
        };
    }

    public override async Task<SearchUserInfoResponse> SearchUserInfo(SearchUserInfoRequest request, ServerCallContext context)
    {
        if (request.HasUserId && request.UserId.Length < 2)
        {
            return new SearchUserInfoResponse();
        }
        if (request.HasUserName && request.UserName.Length == 0)
        {
            return new SearchUserInfoResponse();
        }

        var contactResult = new List<Contact>();
        _engine.Cloud.Contacts.ForeachContact(c =>
        {
            if (request.HasUserId)
            {
                if (request.PartialMatch)
                {
                    if (c.ContactUserId.Contains(request.UserId))
                    {
                        contactResult.Add(c);
                    }
                }
                else
                {
                    if (c.ContactUserId.Equals(request.UserId, StringComparison.InvariantCultureIgnoreCase))
                    {
                        contactResult.Add(c);
                    }
                }
            }
            else
            {
                if (request.PartialMatch)
                {
                    if (c.ContactUsername.Contains(request.UserName.Trim().ToLower(), StringComparison.InvariantCultureIgnoreCase))
                    {
                        contactResult.Add(c);
                    }
                }
                else
                {
                    if (c.ContactUsername.Equals(request.UserName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        contactResult.Add(c);
                    }
                }
            }
        });
        var result = contactResult.Select(c => new Rpc.UserInfo { Id = c.ContactUserId, Name = c.ContactUsername, IconUrl = CloudUtils.ResolveURL(c.Profile?.IconUrl) ?? "" }).ToList();
        if (!request.OnlyInContacts && request.HasUserName)
        {
            var cloudResult = await _engine.Cloud.Users.GetUsers(request.UserName.Trim().ToLower());
            if (cloudResult.IsOK)
            {
                result.Concat(cloudResult.Entity.Select(r => new Rpc.UserInfo { Id = r.Id, Name = r.Username, IconUrl = "" }));
            }
        }
        return new SearchUserInfoResponse
        {
            Users = { result }
        };
    }

    public override Task<GetFriendRequestsResponse> GetFriendRequests(GetFriendRequestsRequest request, ServerCallContext context)
    {
        var contacts = new List<Contact>();
        _engine.Cloud.Contacts.GetContacts(contacts);
        var userInfos = contacts.FindAll(c => c.ContactStatus == ContactStatus.Requested).Select(c => new Rpc.UserInfo
        {
            Id = c.ContactUserId,
            Name = c.ContactUsername,
            IconUrl = CloudUtils.ResolveURL(c.Profile?.IconUrl) ?? ""
        });
        return Task.FromResult(new GetFriendRequestsResponse
        {
            Users = { userInfos }
        });
    }

    public override Task<AcceptFriendRequestsResponse> AcceptFriendRequests(AcceptFriendRequestsRequest request, ServerCallContext context)
    {
        foreach (var userId in request.UserIds)
        {
            var contact = _engine.Cloud.Contacts.GetContact(userId);
            if (contact.ContactStatus == ContactStatus.Requested)
            {
                _engine.Cloud.Contacts.AddContact(contact);
            }
        }
        return Task.FromResult(new AcceptFriendRequestsResponse { });
    }

    public override async Task<SendFriendRequestResponse> SendFriendRequest(SendFriendRequestRequest request, ServerCallContext context)
    {
        var cloud = _engine.Cloud;
        if (cloud.CurrentUser is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Headless is not login"));
        }

        string userId;
        string userName;

        if (request.HasUserId)
        {
            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id must not be empty"));
            }
            // Cloud API を叩く前に self チェックを済ませる
            if (string.Equals(request.UserId, cloud.CurrentUserID, StringComparison.Ordinal))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Cannot send friend request to yourself"));
            }
            userId = request.UserId;
            // Contact に保存されている username は stale の可能性があるため、常に fresh に取得する
            var userResult = await cloud.Users.GetUser(userId);
            if (!userResult.IsOK || userResult.Entity is null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"User with id '{userId}' not found"));
            }
            userName = userResult.Entity.Username;
        }
        else if (request.HasUserName)
        {
            if (string.IsNullOrWhiteSpace(request.UserName))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "user_name must not be empty"));
            }
            // Cloud API を叩く前に self チェックを済ませる
            if (string.Equals(request.UserName, cloud.CurrentUsername, StringComparison.InvariantCultureIgnoreCase))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Cannot send friend request to yourself"));
            }
            // まず contacts から探す (すでに関係があるユーザ)。ContactUsername が null でも NRE しないよう静的呼び出しを使う
            var existingContact = cloud.Contacts.FindContact(c => string.Equals(c.ContactUsername, request.UserName, StringComparison.InvariantCultureIgnoreCase));
            if (existingContact is not null)
            {
                userId = existingContact.ContactUserId;
                userName = existingContact.ContactUsername;
            }
            else
            {
                var userResult = await cloud.Users.GetUserByName(request.UserName);
                if (!userResult.IsOK || userResult.Entity is null)
                {
                    throw new RpcException(new Status(StatusCode.NotFound, $"User with name '{request.UserName}' not found"));
                }
                userId = userResult.Entity.Id;
                userName = userResult.Entity.Username;
            }
        }
        else
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Require valid user_id or user_name"));
        }

        // user_name パスで大文字小文字違い等により self を解決してしまうケースへの safety net
        if (string.Equals(userId, cloud.CurrentUserID, StringComparison.Ordinal))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Cannot send friend request to yourself"));
        }

        // ContactStatus を事前にチェックする。
        // ContactManager.AddContact(id, name) は無条件で ContactStatus = Accepted に上書きするため、
        // このチェック無しでは以下の破壊的挙動が発生する:
        //  - Blocked な相手を無警告で unblock
        //  - Requested (相手からの受信リクエスト) を自動 accept して 'send' 扱い
        //  - Accepted (既にフレンド) をサイレント no-op
        var existing = cloud.Contacts.GetContact(userId);
        if (existing is not null)
        {
            switch (existing.ContactStatus)
            {
                case ContactStatus.Blocked:
                    throw new RpcException(new Status(StatusCode.FailedPrecondition, "User is blocked; unblock first"));
                case ContactStatus.Requested:
                    throw new RpcException(new Status(StatusCode.FailedPrecondition, "This user has already sent you a friend request; use AcceptFriendRequests"));
                case ContactStatus.Accepted:
                    throw new RpcException(new Status(StatusCode.AlreadyExists, "Already in contacts"));
                    // Ignored / None / SearchResult は送信続行
            }
        }

        var result = await cloud.Contacts.AddContact(userId, userName);
        if (!result)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed to send friend request"));
        }

        return new SendFriendRequestResponse();
    }

    public override async Task<RemoveContactResponse> RemoveContact(RemoveContactRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.UserId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id is required"));
        }
        if (_engine.Cloud.CurrentUser is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Headless is not login"));
        }

        var contact = _engine.Cloud.Contacts.GetContact(request.UserId);
        if (contact is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Contact not found: {request.UserId}"));
        }

        await _engine.Cloud.Contacts.RemoveContact(contact);
        return new RemoveContactResponse();
    }

    public override Task<ListContactsResponse> ListContacts(ListContactsRequest request, ServerCallContext context)
    {
        var contacts = new List<Contact>();
        _engine.Cloud.Contacts.GetContacts(contacts);

        // ページネーション処理
        int startIndex = 0;
        if (!string.IsNullOrEmpty(request.Cursor) && int.TryParse(request.Cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

        var pagedContacts = contacts.Skip(startIndex).Take(request.Limit + 1).ToList();
        var hasMore = pagedContacts.Count > request.Limit;
        var resultContacts = pagedContacts.Take(request.Limit);

        var userInfos = resultContacts.Select(c => new Rpc.UserInfo
        {
            Id = c.ContactUserId,
            Name = c.ContactUsername,
            IconUrl = CloudUtils.ResolveURL(c.Profile?.IconUrl) ?? ""
        });

        var response = new ListContactsResponse
        {
            Users = { userInfos }
        };
        if (hasMore)
        {
            response.NextCursor = (startIndex + request.Limit).ToString();
        }
        return Task.FromResult(response);
    }

    public override async Task<GetContactMessagesResponse> GetContactMessages(GetContactMessagesRequest request, ServerCallContext context)
    {
        // GetMessages returns messages in newest-first order (index 0 = newest)
        // fromTime fetches messages NEWER than the specified time
        const int maxFetchLimit = 100;
        const int maxRetries = 10;
        var fetchLimit = Math.Min(request.Limit + 1, maxFetchLimit);

        var targetId = !string.IsNullOrEmpty(request.BeforeId) ? request.BeforeId : request.AfterId;

        // Track if there are more older messages in the cloud beyond what we fetched
        bool moreOlderMessagesInCloud = false;

        // First, fetch from the latest (fromTime = null)
        var result = await _engine.Cloud.Messages.GetMessages(null, fetchLimit, request.UserId, false);
        if (result.IsError)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed fetch from resonite cloud"));
        }

        var allMessages = result.Entity.ToList();

        // If we got fetchLimit messages, there might be more older messages
        if (allMessages.Count >= fetchLimit)
        {
            moreOlderMessagesInCloud = true;
        }

        // If target specified and not found, search older messages by going back in time
        if (!string.IsNullOrEmpty(targetId) && !allMessages.Any(m => m.Id == targetId) && allMessages.Count > 0)
        {
            var oldestFetchedTime = allMessages.Last().SendTime;

            // Start searching from 1 month back, then keep going back
            var searchFromTime = DateTime.UtcNow.AddMonths(-1);

            for (int retry = 0; retry < maxRetries; retry++)
            {
                result = await _engine.Cloud.Messages.GetMessages(searchFromTime, fetchLimit, request.UserId, false);
                if (result.IsError)
                {
                    throw new RpcException(new Status(StatusCode.Internal, "Failed fetch from resonite cloud"));
                }

                var fetchedMessages = result.Entity;
                if (fetchedMessages.Count == 0)
                {
                    // No messages in this time range, go back further
                    searchFromTime = searchFromTime.AddMonths(-1);
                    continue;
                }

                var newestFetchedTime = fetchedMessages.First().SendTime;
                var oldestInBatchTime = fetchedMessages.Last().SendTime;

                // Add messages that don't overlap with what we already have
                var messagesToAdd = fetchedMessages.Where(m => m.SendTime < oldestFetchedTime).ToList();
                allMessages.AddRange(messagesToAdd);
                oldestFetchedTime = allMessages.Last().SendTime;

                // Update moreOlderMessagesInCloud based on this fetch
                moreOlderMessagesInCloud = fetchedMessages.Count >= fetchLimit;

                // Check if we found the target
                if (allMessages.Any(m => m.Id == targetId))
                {
                    break;
                }

                // Check if ranges have connected (newest in this batch reaches our oldest)
                if (newestFetchedTime >= oldestFetchedTime || oldestInBatchTime >= oldestFetchedTime)
                {
                    // Ranges connected - if target not found, it doesn't exist
                    break;
                }

                // Move search window further back
                searchFromTime = searchFromTime.AddMonths(-1);
            }

            // Sort all messages by SendTime descending (newest first)
            allMessages = allMessages.OrderByDescending(m => m.SendTime).ToList();
        }

        int startIndex = 0;
        int endIndex = allMessages.Count;
        bool hasMoreBefore = false;
        bool hasMoreAfter = false;

        if (!string.IsNullOrEmpty(request.BeforeId))
        {
            // before_id: get messages OLDER than the specified ID
            var beforeIndex = allMessages.FindIndex(m => m.Id == request.BeforeId);
            if (beforeIndex < 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Message with before_id '{request.BeforeId}' not found"));
            }
            startIndex = beforeIndex + 1; // Start after the specified message (older messages)
            hasMoreAfter = true; // There are newer messages (at least the before_id message)
        }
        else if (!string.IsNullOrEmpty(request.AfterId))
        {
            // after_id: get messages NEWER than the specified ID
            var afterIndex = allMessages.FindIndex(m => m.Id == request.AfterId);
            if (afterIndex < 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Message with after_id '{request.AfterId}' not found"));
            }
            endIndex = afterIndex; // End before the specified message (newer messages only)
            hasMoreBefore = true; // There are older messages (at least the after_id message)
        }

        // Slice the messages based on calculated indices
        var filteredMessages = allMessages.Skip(startIndex).Take(endIndex - startIndex).ToList();

        // Check if there are more messages beyond the limit
        if (filteredMessages.Count > request.Limit)
        {
            hasMoreBefore = true;
            filteredMessages = filteredMessages.Take(request.Limit).ToList();
        }

        // Check if there are more older messages (either in allMessages or in the cloud)
        if (startIndex + filteredMessages.Count < allMessages.Count)
        {
            hasMoreBefore = true;
        }
        else if (moreOlderMessagesInCloud && endIndex == allMessages.Count)
        {
            // We're at the end of allMessages but cloud might have more
            hasMoreBefore = true;
        }

        // Convert to proto messages (keep newest-first order for response)
        var protoMessages = filteredMessages.Select(m => new Rpc.ContactChatMessage
        {
            Id = m.Id,
            Type = m.MessageType switch
            {
                SkyFrost.Base.MessageType.Object => ContactChatMessageType.Object,
                SkyFrost.Base.MessageType.Sound => ContactChatMessageType.Sound,
                SkyFrost.Base.MessageType.Text => ContactChatMessageType.Text,
                SkyFrost.Base.MessageType.SessionInvite => ContactChatMessageType.SessionInvite,
                _ => ContactChatMessageType.Unspecified
            },
            Content = m.Content,
            SendTime = Timestamp.FromDateTime(m.SendTime),
            ReadTime = m.ReadTime is not null ? Timestamp.FromDateTime((DateTime)m.ReadTime) : null,
            SenderId = m.SenderId,
        });

        return new GetContactMessagesResponse
        {
            Messages = { protoMessages },
            HasMoreBefore = hasMoreBefore,
            HasMoreAfter = hasMoreAfter,
        };
    }

    public override async Task<SendContactMessageResponse> SendContactMessage(SendContactMessageRequest request, ServerCallContext context)
    {
        await _engine.Cloud.Messages.GetUserMessages(request.UserId).SendTextMessage(request.Message);

        return new SendContactMessageResponse();
    }
}
