using LiveHelper.Models;
using Newtonsoft.Json;
using System;

namespace LiveHelper.Overlay
{
    public class OverlayServer(ushort webSocketPort, ushort webPort, string html, SessionState state, Action<string> log) : IDisposable
    {
        private WebServer WebServer { get; set; } = new(webPort, html, log);

        private WebsocketServer WebsocketServer { get; set; } = new(webSocketPort, () => JsonConvert.SerializeObject(state.Snapshot_Sync), log);

        private bool Started { get; set; }

        public void Start()
        {
            if (Started)
            {
                return;
            }

            WebsocketServer.Start();
            try
            {
                WebServer.Start();
            }
            catch
            {
                WebsocketServer.Stop();
                throw;
            }

            state.OnSnapshotChanged += Broadcast;
            Started = true;
        }

        public void Stop()
        {
            Started = false;
            state.OnSnapshotChanged -= Broadcast;
            try
            {
                WebsocketServer.Stop();
            }
            finally
            {
                WebServer.Stop();
            }
        }

        private void Broadcast(OverlaySnapshot snapshot) => WebsocketServer.Broadcast(JsonConvert.SerializeObject(snapshot));

        public void Dispose() => Stop();
    }
}