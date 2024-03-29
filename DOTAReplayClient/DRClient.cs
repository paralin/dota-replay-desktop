﻿using System;
using System.Collections.Generic;
using DOTAReplayClient.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;

namespace DOTAReplayClient
{
    public class DRClient
    {
        private const string VERSION = "1.9.3";

        private static readonly log4net.ILog log =
            log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private WebSocket socket;
        public event EventHandler<bool> HandshakeResponse;
        public event EventHandler ConnectionClosed;
        public event EventHandler NotAReviewer;
        public event EventHandler<Submission> AddUpdateSub;
        public event EventHandler<string> RemoveSub;
        public event EventHandler<UserInfo> OnUserInfo;
        public event EventHandler<SystemStats> OnSystemStats;
        public event EventHandler OnInvalidVersion;

        private Dictionary<string, Action<bool, string, double, double>> downloadAction =
            new Dictionary<string, Action<bool, string, double, double>>();

        private Action<bool, string> requestMoreAction;


        public DRClient()
        {
//#if DEBUG
//            socket = new WebSocket("ws://192.168.1.46:10304");
//#else
            socket = new WebSocket("ws://replay.paral.in:10304");
//#endif
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
            log.Warn("Websocket error: " + errorEventArgs.Message);
        }

        private void ProcessMessage(object sender, MessageEventArgs messageEventArgs)
        {
            if (messageEventArgs.Data.Length == 0)
            {
                log.Debug("Ignoring empty data....");
                return;
            }
            log.Debug("Message: " + messageEventArgs.Data);
            JObject obj = JObject.Parse(messageEventArgs.Data);
            switch ((ServerMessageIDs) obj["m"].Value<int>())
            {
                case ServerMessageIDs.HANDSHAKE_RESPONSE:
                    log.Debug("Handshake success: " + obj["success"].Value<bool>());
                    HandshakeResponse?.Invoke(this, obj["success"].Value<bool>());
                    break;
                case ServerMessageIDs.INVALID_VERSION:
                    log.Debug("Invalid version! Our version: " + VERSION);
                    OnInvalidVersion?.Invoke(this, EventArgs.Empty);
                    break;
                case ServerMessageIDs.UNABLE_TO_VIEW:
                    log.Debug("We aren't allowed to view replays.");
                    NotAReviewer?.Invoke(this, new EventArgs());
                    break;
                case ServerMessageIDs.ADD_UPDATE_SUB:
                    log.Debug("Received addition / update of submission.");
                    var suba = obj["replay"].ToObject<Submission>();
                    suba.Id = obj["replay"]["_id"].Value<string>();
                    AddUpdateSub?.Invoke(this, suba);
                    break;
                case ServerMessageIDs.REMOVE_SUB:
                    log.Debug("Received removal of submission.");
                    var subi = obj["id"].Value<string>();
                    RemoveSub?.Invoke(this, subi);
                    break;
                case ServerMessageIDs.USER_INFO:
                    log.Debug("Received user information.");
                    var useri = obj["user"].ToObject<UserInfo>();
                    log.Debug("User info: " + JObject.FromObject(useri).ToString(Formatting.Indented));
                    OnUserInfo?.Invoke(this, useri);
                    break;
                case ServerMessageIDs.SYSTEM_STATS:
                    log.Debug("Received system stats.");
                    var stats = obj.ToObject<SystemStats>();
                    OnSystemStats?.Invoke(this, stats);
                    break;
                case ServerMessageIDs.REQUEST_REPLYR:
                {
                    log.Debug("Received download request response.");
                    var success = obj["success"].Value<bool>();
                    string data = null;
                    double matchid = 0;
                    double matchtime = 0;
                    if (success)
                    {
                        data = obj["url"].Value<string>();
                        matchid = obj["matchid"].Value<double>();
                        matchtime = obj["matchtime"].Value<double>();
                    }
                    else data = obj["reason"].Value<string>();
                    downloadAction[obj["id"].Value<string>()].Invoke(success, data, matchid, matchtime);
                    downloadAction.Remove(obj["id"].Value<string>());
                    break;
                }
                case ServerMessageIDs.REQREV_REPLYR:
                {
                    log.Debug("Received response to more review request.");
                    var success = obj["success"].Value<bool>();
                    string data = null;
                    if (!success) data = obj["reason"].Value<string>();
                    requestMoreAction.Invoke(success, data);
                    break;
                }
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
            ConnectionClosed?.Invoke(this, new EventArgs());
        }

        public void SendHandshake()
        {
            log.Debug("Sending handshake....");
            socket.Send(new
            {
                m = MessageIDs.HANDSHAKE,
                token = Settings.Default.Token,
                version = VERSION
            });
        }

        public void Stop()
        {
            log.Debug("Disconnecting");
            socket.Close(CloseStatusCode.Normal);
        }

        public void RequestDownloadURL(string id, Action<bool, string, double, double> action, bool isMatchid = false)
        {
            downloadAction[id] = action;
            socket.Send(new
            {
                m = MessageIDs.REQUEST_DOWNLOAD,
                id,
                matchid = isMatchid
            });
        }

        public void SendRequestMore(Action<bool, string> cb)
        {
            this.requestMoreAction = cb;
            socket.Send(new
            {
                m = MessageIDs.REQUEST_REVIEW
            });
        }

        public void SendReview(MainWindow.ReviewRequest data)
        {
            socket.Send(new
            {
                m = MessageIDs.SUBMIT_REVIEW,
                rating = data.Rating,
                descrip = data.Description,
                id = data.Id
            });
        }
    }
}