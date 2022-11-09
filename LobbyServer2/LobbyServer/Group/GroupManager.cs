﻿using System;
using System.Collections.Generic;
using System.Linq;
using CentralServer.LobbyServer.Session;
using EvoS.Framework.DataAccess;
using EvoS.Framework.Network.Static;
using log4net;

namespace CentralServer.LobbyServer.Group
{
    class GroupManager
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(GroupManager));
        
        private static readonly Dictionary<long, GroupInfo> ActiveGroups = new Dictionary<long, GroupInfo>();
        private static readonly Dictionary<long, long> PlayerToGroup = new Dictionary<long, long>();
        private static readonly Dictionary<long, GroupRequestInfo> GroupRequests = new Dictionary<long, GroupRequestInfo>();
        private static long _nextGroupId = 0;
        private static long _nextGroupRequestId = 0;
        private static readonly object _lock = new object();

        public static GroupInfo GetPlayerGroup(long accountId)
        {
            lock (_lock)
            {
                return PlayerToGroup.TryGetValue(accountId, out long groupId)
                    ? ActiveGroups[groupId]
                    : null;
            }
        }
        
        public static long CreateGroupRequest(long requesterAccountId, long requesteeAccountId, long groupId)
        {
            lock (_lock)
            {
                if (!ActiveGroups.ContainsKey(groupId))
                {
                    throw new ArgumentException("Invalid group id");
                }

                long requestId = _nextGroupRequestId++;
                // TODO
                // GroupRequests.Add(
                //     requestId,
                //     new GroupRequestInfo(requestId, requesterAccountId, requesteeAccountId, groupId));
                return requestId;
            }
        }
        
        public static void CreateGroup(long leader) {
            LeaveGroup(leader, false);
            long groupId;
            lock (_lock)
            {
                groupId = _nextGroupId++;
                ActiveGroups.Add(groupId, new GroupInfo(groupId));
            }
            JoinGroup(groupId, leader);
        }

        public static void LeaveGroup(long accountId, bool warnIfNotInAGroup = true)
        {
            GroupInfo leftGroup = null;
            lock (_lock)
            {
                if (PlayerToGroup.TryGetValue(accountId, out long groupId))
                {
                    GroupInfo groupInfo = ActiveGroups[groupId];
                    groupInfo.RemovePlayer(accountId);
                    PlayerToGroup.Remove(accountId);
                    log.Info($"Removed {accountId} from group {groupId}");
                    if (groupInfo.IsEmpty())
                    {
                        PlayerToGroup.Remove(groupInfo.Leader);
                        ActiveGroups.Remove(groupId);
                        log.Info($"Group {groupId} disbanded");
                    }
                    leftGroup = groupInfo;
                }
                else if (warnIfNotInAGroup)
                {
                    log.Warn($"Player {accountId} attempted to leave a group while not being in one");
                }
            }

            if (leftGroup != null)
            {
                OnLeaveGroup(accountId);
                BroadcastUpdate(leftGroup);
            }
        }

        public static void JoinGroup(long groupId, long accountId)
        {
            LeaveGroup(accountId, false);
            GroupInfo joinedGroup = null;
            lock (_lock)
            {
                if (ActiveGroups.TryGetValue(groupId, out GroupInfo groupInfo))
                {
                    groupInfo.AddPlayer(accountId);
                    PlayerToGroup.Add(accountId, groupId);
                    log.Info($"Added {accountId} to group {groupId}");
                    joinedGroup = groupInfo;
                }
                else
                {
                    log.Error($"Player {accountId} attempted to join a non-existing group {groupId}");
                }
            }

            if (joinedGroup != null)
            {
                OnJoinGroup(accountId);
                BroadcastUpdate(joinedGroup);
            }
        }

        private static UpdateGroupMemberData GetMemberData(GroupInfo groupInfo, long accountId)
        {
            PersistedAccountData account = DB.Get().AccountDao.GetAccount(accountId);
            LobbyServerProtocol session = SessionManager.GetClientConnection(accountId);
            CharacterComponent characterComponent = account.CharacterData[account.AccountComponent.LastCharacter].CharacterComponent;

            return new UpdateGroupMemberData()
            {
                MemberDisplayName = account.Handle,
                MemberHandle = account.Handle,
                HasFullAccess = true,
                IsLeader = groupInfo.IsLeader(account.AccountId),
                IsReady = session.IsReady,
                IsInGame = false, // TODO
                // CreateGameTimestamp = session.CreateGameTimestamp,
                AccountID = account.AccountId,
                MemberDisplayCharacter = account.AccountComponent.LastCharacter,
                VisualData = new GroupMemberVisualData
                {
                    VisualInfo = characterComponent.LastSkin,
                    ForegroundBannerID = account.AccountComponent.SelectedForegroundBannerID,
                    BackgroundBannerID = account.AccountComponent.SelectedBackgroundBannerID,
                    TitleID = account.AccountComponent.SelectedTitleID,
                    RibbonID = account.AccountComponent.SelectedRibbonID,
                },
                PenaltyTimeout = DateTime.MinValue,
                GameLeavingPoints = 0
            };
        }
        
        public static LobbyPlayerGroupInfo GetGroupInfo(long accountId)
        {
            GroupInfo groupInfo = null;
            lock (_lock)
            {
                if (PlayerToGroup.TryGetValue(accountId, out long groupId))
                {
                    groupInfo = ActiveGroups[groupId];
                }
            }

            PersistedAccountData account = DB.Get().AccountDao.GetAccount(accountId);
            LobbyServerProtocol client = SessionManager.GetClientConnection(accountId);
            LobbyPlayerGroupInfo response;
            if (groupInfo == null)
            {
                response = new LobbyPlayerGroupInfo
                {
                    SelectedQueueType = client.SelectedGameType,
                    MemberDisplayName = account.Handle,
                    InAGroup = false,
                    IsLeader = true,
                    Members = new List<UpdateGroupMemberData>(),
                };
            }
            else
            {
                LobbyServerProtocol leader = SessionManager.GetClientConnection(groupInfo.Leader);
                response = new LobbyPlayerGroupInfo
                {
                    SelectedQueueType = leader.SelectedGameType,
                    SubTypeMask = leader.SelectedSubTypeMask,
                    MemberDisplayName = account.Handle,
                    InAGroup = true,
                    IsLeader = groupInfo.IsLeader(account.AccountId),
                    Members = groupInfo.Members.Select(id => GetMemberData(groupInfo, id)).ToList()
                };
            }
            response.SetCharacterInfo(LobbyCharacterInfo.Of(account.CharacterData[account.AccountComponent.LastCharacter]));
            return response;
        }

        private static void OnJoinGroup(long accountId)
        {
            SessionManager.GetClientConnection(accountId)?.OnJoinGroup();
        }

        private static void OnLeaveGroup(long accountId)
        {
            SessionManager.GetClientConnection(accountId)?.OnLeaveGroup();
        }

        private static void BroadcastUpdate(GroupInfo groupInfo)
        {
            SessionManager.GetClientConnection(groupInfo.Leader)?.BroadcastRefreshGroup();
        }
    }
}
