﻿using System.Collections.Generic;
using System.Linq;
using CentralServer.LobbyServer.Session;
using EvoS.Framework.Constants.Enums;
using EvoS.Framework.DataAccess;
using EvoS.Framework.Network.NetworkMessages;
using EvoS.Framework.Network.Static;

namespace CentralServer.LobbyServer.Friend
{
    class FriendManager
    {
        public static FriendStatusNotification GetFriendStatusNotification(long accountId)
        {
            FriendStatusNotification notification = new FriendStatusNotification()
            {
                FriendList = GetFriendList(accountId)
            };

            return notification;
        }

        public static FriendList GetFriendList(long accountId)
        {
            FriendList friendList = new FriendList
            {
                // TODO We are all friends here for now
                Friends = SessionManager.GetOnlinePlayers()
                    .Where(id => id != accountId)
                    .Select(id => DB.Get().AccountDao.GetAccount(id))
                    .ToDictionary(acc => acc.AccountId,
                        acc => new FriendInfo()
                        {
                            FriendAccountId = acc.AccountId,
                            FriendHandle = acc.Handle,
                            FriendStatus = FriendStatus.Friend,
                            IsOnline = true,
                            StatusString = GetStatusString(SessionManager.GetClientConnection(acc.AccountId)),
                            // FriendNote = 
                            BannerID = acc.AccountComponent.SelectedBackgroundBannerID,
                            EmblemID = acc.AccountComponent.SelectedForegroundBannerID,
                            TitleID = acc.AccountComponent.SelectedTitleID,
                            TitleLevel = acc.AccountComponent.TitleLevels.GetValueOrDefault(acc.AccountComponent.SelectedTitleID, 0),
                            RibbonID = acc.AccountComponent.SelectedRibbonID,
                        }),
                IsDelta = false
            };

            return friendList;
        }

        private static string GetStatusString(LobbyServerProtocol client)
        {
            if (client == null)
            {
                return "Offline";
            }
            if (client.IsInGame())
            {
                return "In Game";
            }
            if (client.IsInQueue())
            {
                return "Queued";
            }
            if (client.IsInGroup())
            {
                return "GroupChatRoom";  // No localization for "In Group" status so we have to borrow this one
            }
            return string.Empty;
        }

        public static PlayerUpdateStatusResponse OnPlayerUpdateStatusRequest(LobbyServerProtocol client, PlayerUpdateStatusRequest request)
        {
            // TODO: notify this client's friends the status change

            PlayerUpdateStatusResponse response = new PlayerUpdateStatusResponse()
            {
                AccountId = client.AccountId,
                StatusString = request.StatusString,
                ResponseId = request.RequestId
            };

            return response;
        }
    }
}
