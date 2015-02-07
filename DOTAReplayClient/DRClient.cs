﻿using System;
using System.Globalization;
using DOTAReplayClient.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;

namespace DOTAReplayClient
{
    public class DRClient
    {
        private static readonly log4net.ILog log =
            log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private WebSocket socket;
        public event EventHandler<bool> HandshakeResponse;
        public event EventHandler       ConnectionClosed;
        public event EventHandler       NotAReviewer;
        public event EventHandler<Submission> AddUpdateSub;
        public event EventHandler<string> RemoveSub;
        public event EventHandler<UserInfo> OnUserInfo;
        public event EventHandler<SystemStats> OnSystemStats;
 
        public DRClient()
        {
#if DEBUG
            socket = new WebSocket("ws://192.168.1.46:10304");
#else
            socket = new WebSocket("ws://replay.paral.in:10304");
#endif
            socket.OnClose += OnSocketClose;
            socket.OnOpen += OnSocketOpen;
            socket.OnMessage += ProcessMessage;
            socket.OnError += HandleError;
            log.Debug("Setting up socket...");
        }

        public void Start()
        {
            log.Debug("Connecting to server...");
            socket.ConnectAsync();
        }

        private void HandleError(object sender, ErrorEventArgs errorEventArgs)
        {
            log.Warn("Websocket error: "+errorEventArgs.Message);
        }

        private void ProcessMessage(object sender, MessageEventArgs messageEventArgs)
        {
            if (messageEventArgs.Data.Length == 0)
            {
                log.Debug("Ignoring empty data....");
                return;
            }
            log.Debug("Message: "+messageEventArgs.Data);
            JObject obj = JObject.Parse(messageEventArgs.Data);
            switch ((ServerMessageIDs)obj["m"].Value<int>())
            {
                case ServerMessageIDs.HANDSHAKE_RESPONSE:
                    log.Debug("Handshake success: "+obj["success"].Value<bool>());
                    if (HandshakeResponse != null) HandshakeResponse(this, obj["success"].Value<bool>());
                    break;
                case ServerMessageIDs.UNABLE_TO_VIEW:
                    log.Debug("We aren't allowed to view replays.");
                    if(NotAReviewer != null) NotAReviewer(this, new EventArgs());
                    break;
                case ServerMessageIDs.ADD_UPDATE_SUB:
                    log.Debug("Received addition / update of submission.");
                    var suba = obj["replay"].ToObject<Submission>();
                    suba.Id = obj["replay"]["_id"].Value<string>();
                    if (AddUpdateSub != null) AddUpdateSub(this, suba);
                    break;
                case ServerMessageIDs.REMOVE_SUB:
                    log.Debug("Received removal of submission.");
                    var subi = obj["id"].Value<string>();
                    if (RemoveSub != null) RemoveSub(this, subi);
                    break;
                case ServerMessageIDs.USER_INFO:
                    log.Debug("Received user information.");
                    var useri = obj["user"].ToObject<UserInfo>();
                    log.Debug("User info: "+JObject.FromObject(useri).ToString(Formatting.Indented));
                    if (OnUserInfo != null) OnUserInfo(this, useri);
                    break;
                case ServerMessageIDs.SYSTEM_STATS:
                    log.Debug("Received system stats.");
                    var stats = new SystemStats() {allSubmissions = obj["submissions"].Value<int>()};
                    if (OnSystemStats != null) OnSystemStats(this, stats);
                    break;
            }
        }

        private void OnSocketOpen(object sender, EventArgs eventArgs)
        {
            log.Info("Socket with server opened.");
            SendHandshake();
        }

        private void OnSocketClose(object sender, CloseEventArgs closeEventArgs)
        {
            log.Info("Socket with server closed!");
            if (ConnectionClosed != null) ConnectionClosed(this, new EventArgs());
        }

        public void SendHandshake()
        {
            log.Debug("Sending handshake....");
            socket.Send(new
            {
                m = MessageIDs.HANDSHAKE,
                token = Settings.Default.Token
            });
        }

        public void Stop()
        {
            log.Debug("Disconnecting");
            socket.Close(CloseStatusCode.Normal);
        }
    }
}