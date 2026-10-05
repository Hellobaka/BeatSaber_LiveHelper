using System;
using System.Collections.Generic;
using System.Net;
using WebSocketSharp;
using SocketServer = WebSocketSharp.Server.WebSocketServer;

namespace LiveHelper.Overlay
{
    public sealed class WebsocketServer : IDisposable
    {
        private object syncLock = new();

        public bool Started { get; set; }

        private SocketServer SocketServer { get; set; }

        private Func<string> GetSnapshot { get; set; }

        private Action<string> OnLog { get; set; }

        private List<OverlayWebSocketClient> Clients { get; set; } = [];

        public WebsocketServer(ushort port, Func<string> getSnapshot, Action<string> log)
        {
            GetSnapshot = getSnapshot;
            OnLog = log;
            SocketServer = new SocketServer(IPAddress.Loopback, port);
            SocketServer.Log.Level = LogLevel.Warn;
            SocketServer.Log.Output = (data, _) => Log("WebSocket server: " + data.Message);
            SocketServer.AddWebSocketService<OverlayWebSocketClient>("/ws", client => client.Initialize(this));
        }

        public void Start()
        {
            if (Started)
            {
                return;
            }

            SocketServer.Start();
            if (!SocketServer.IsListening)
            {
                throw new InvalidOperationException("Cannot start overlay WebSocket server.");
            }

            Started = true;
        }

        public void Stop()
        {
            if (!Started)
            {
                return;
            }

            Started = false;
            try
            {
                SocketServer.Stop();
            }
            finally
            {
                lock (syncLock)
                {
                    foreach (var client in Clients)
                    {
                        client.Dispose();
                    }
                    Clients.Clear();
                }
            }
        }

        public void Broadcast(string json)
        {
            lock (syncLock)
            {
                foreach (var client in Clients)
                {
                    client.Queue(json);
                }
            }
        }

        public void AddClient(OverlayWebSocketClient client)
        {
            lock (syncLock)
            {
                Clients.Add(client);
                client.Queue(GetSnapshot());
            }
        }

        public void RemoveClient(OverlayWebSocketClient client)
        {
            lock (syncLock)
            {
                Clients.Remove(client);
            }
        }

        public void Log(string message) => OnLog(message);

        public void Dispose() => Stop();
    }
}