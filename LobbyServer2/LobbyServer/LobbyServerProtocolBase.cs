﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CentralServer.BridgeServer;
using CentralServer.LobbyServer.Character;
using CentralServer.LobbyServer.Config;
using CentralServer.LobbyServer.Friend;
using CentralServer.LobbyServer.Gamemode;
using CentralServer.LobbyServer.Group;
using CentralServer.LobbyServer.Matchmaking;
using CentralServer.LobbyServer.Quest;
using EvoS.Framework.Constants.Enums;
using EvoS.Framework.DataAccess;
using EvoS.Framework.Misc;
using EvoS.Framework.Network;
using EvoS.Framework.Network.NetworkMessages;
using EvoS.Framework.Network.Static;
using EvoS.Framework.Network.WebSocket;
using log4net;
using WebSocketSharp;

namespace CentralServer.LobbyServer
{
    public class LobbyServerProtocolBase : WebSocketBehaviorBase
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(LobbyServerProtocolBase));
        private Dictionary<Type, EvosMessageDelegate<WebSocketMessage>> messageHandlers = new Dictionary<Type, EvosMessageDelegate<WebSocketMessage>>();
        public long AccountId;
        public long SessionToken;
        public string UserName;
        
        public GameType SelectedGameType;
        public ushort SelectedSubTypeMask;

        public BridgeServerProtocol CurrentServer { get; set; }

        protected override string GetConnContext()
        {
            return "C " + AccountId;
        }

        protected override void HandleMessage(MessageEventArgs e)
        {
            MemoryStream stream = new MemoryStream(e.RawData);
            WebSocketMessage deserialized = null;

            try
            {
                deserialized = (WebSocketMessage)EvosSerializer.Instance.Deserialize(stream);
            }
            catch (NullReferenceException nullEx)
            {
                log.Error("No message handler registered for data: " + BitConverter.ToString(e.RawData));
            }

            if (deserialized != null)
            {
                EvosMessageDelegate<WebSocketMessage> handler = GetHandler(deserialized.GetType());
                if (handler != null)
                {
                    log.Debug($"< {deserialized.GetType().Name} {DefaultJsonSerializer.Serialize(deserialized)}");
                    handler.Invoke(deserialized);
                }
                else
                {
                    log.Error("No handler for " + deserialized.GetType().Name + "\n" + DefaultJsonSerializer.Serialize(deserialized));
                }
            }
        }

        public void RegisterHandler<T>(EvosMessageDelegate<T> handler) where T : WebSocketMessage
        {
            messageHandlers.Add(typeof(T), msg => { handler((T)msg); });
        }

        private EvosMessageDelegate<WebSocketMessage> GetHandler(Type type)
        {
            try
            {
                return messageHandlers[type];
            }
            catch (KeyNotFoundException e)
            {
                log.Error("No handler found for type " + type.Name);
                return null;
            }
        }

        public void Send(WebSocketMessage message)
        {
            MemoryStream stream = new MemoryStream();
            EvosSerializer.Instance.Serialize(stream, message);
            this.Send(stream.ToArray());
            log.Debug($"> {message.GetType().Name} {DefaultJsonSerializer.Serialize(message)}");
        }

        public void Broadcast(WebSocketMessage message)
        {
            MemoryStream stream = new MemoryStream();
            EvosSerializer.Instance.Serialize(stream, message);
            Sessions.Broadcast(stream.ToArray());
            log.Debug($">> {message.GetType().Name} {DefaultJsonSerializer.Serialize(message)}");
        }


        public void SendErrorResponse(WebSocketResponseMessage response, int requestId, string message)
        {
            response.Success = false;
            response.ErrorMessage = message;
            response.ResponseId = requestId;
            log.Error(message);
            Send(response);
        }

        public void SendErrorResponse(WebSocketResponseMessage response, int requestId, Exception error)
        {
            response.Success = false;
            response.ErrorMessage = error.Message;
            response.ResponseId = requestId;
            log.Error(error.Message);
            Console.WriteLine(error);
            Send(response);
        }

        public void SendLobbyServerReadyNotification()
        {
            PersistedAccountData account = DB.Get().AccountDao.GetAccount(AccountId);
            LobbyServerReadyNotification notification = new LobbyServerReadyNotification
            {
                AccountData = account.CloneForClient(),
                AlertMissionData = new LobbyAlertMissionDataNotification(),
                CharacterDataList = account.CharacterData.Values.ToList(),
                CommerceURL = "http://127.0.0.1/AtlasCommerce",
                EnvironmentType = EnvironmentType.External,
                FactionCompetitionStatus = new FactionCompetitionNotification(),
                FriendStatus = FriendManager.GetFriendStatusNotification(this.AccountId),
                GroupInfo = GroupManager.GetGroupInfo(this.AccountId),
                SeasonChapterQuests = QuestManager.GetSeasonQuestDataNotification(),
                ServerQueueConfiguration = GetServerQueueConfigurationUpdateNotification(),
                Status = GetLobbyStatusNotification()
            };

            Send(notification);
        }

        private ServerQueueConfigurationUpdateNotification GetServerQueueConfigurationUpdateNotification()
        {
            return new ServerQueueConfigurationUpdateNotification
            {
                FreeRotationAdditions = new Dictionary<CharacterType, RequirementCollection>(),
                GameTypeAvailabilies = GameModeManager.GetGameTypeAvailabilities(),
                TierInstanceNames = new List<LocalizationPayload>(),
                AllowBadges = true,
                NewPlayerPvPQueueDuration = 0
            };
        }

        private LobbyStatusNotification GetLobbyStatusNotification()
        {
            return new LobbyStatusNotification
            {
                AllowRelogin = false,
                ClientAccessLevel = ClientAccessLevel.Full,
                ErrorReportRate = new TimeSpan(0, 3, 0),
                GameplayOverrides = GetGameplayOverrides(),
                HasPurchasedGame = true,
                PacificNow = DateTime.UtcNow, // TODO ?
                UtcNow = DateTime.UtcNow,
                ServerLockState = ServerLockState.Unlocked,
                ServerMessageOverrides = GetServerMessageOverrides()
            };
        }

        private ServerMessageOverrides GetServerMessageOverrides()
        {
            return new ServerMessageOverrides
            {
                MOTDPopUpText = ConfigManager.MOTDPopUpText, // Popup message when client connects to lobby
                MOTDText = ConfigManager.MOTDText, // "alert" text
                ReleaseNotesHeader = ConfigManager.PatchNotesHeader,
                ReleaseNotesDescription = ConfigManager.PatchNotesDescription,
                ReleaseNotesText = ConfigManager.PatchNotesText,
            };
        }

        public LobbyGameplayOverrides GetGameplayOverrides()
        {
            return new LobbyGameplayOverrides
            {
                AllowReconnectingToGameInstantly = true,
                AllowSpectators = false,
                AllowSpectatorsOutsideCustom = false,
                CharacterConfigs = CharacterConfigs.Characters,
                //CharacterSkinConfigOverrides = null TODO: maybe can be used to unlock all skins
                EnableAllMods = true,
                EnableAllAbilityVfxSwaps = true,
                EnableCards = true,
                EnableClientPerformanceCollecting = false,
                EnableDiscord = false,
                EnableDiscordSdk = false,
                EnableEventBonus = false,
                EnableFacebook = false,
                EnableHiddenCharacters = false,
                EnableMods = true,
                EnableSeasons = false,
                EnableShop = true,
                EnableQuests = false,
                EnableSteamAchievements = false,
                EnableTaunts = true
            };
        }

        

        protected void SetGameType(GameType gameType)
        {
            SelectedGameType = gameType;
        }

        protected void SetAllyDifficulty(BotDifficulty difficulty)
        {
            // TODO
        }
        protected void SetContextualReadyState(ContextualReadyState contextualReadyState)
        {
            log.Debug("SetContextualReadyState");
            MatchmakingManager.StartPractice(this);
        }
        protected void SetEnemyDifficulty(BotDifficulty difficulty)
        {
            // TODO
        }
        protected void SetLastSelectedLoadout(int lastSelectedLoadout)
        {
            log.Debug("last selected loadout changed to " + lastSelectedLoadout);
        }

    }
}
