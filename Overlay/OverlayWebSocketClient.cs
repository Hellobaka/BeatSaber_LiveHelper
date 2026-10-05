using System;
using System.Collections.Generic;
using System.Threading;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace LiveHelper.Overlay
{
    public class OverlayWebSocketClient : WebSocketBehavior, IDisposable
    {
        private object syncLock = new();

        private ManualResetEvent Signal { get; set; } = new(false);

        private Queue<string> QueuedMessages { get; set; } = new();

        private bool Closed { get; set; }

        private WebsocketServer Parent { get; set; } = null!;

        public void Initialize(WebsocketServer owner)
        {
            Parent = owner;
        }

        protected override void OnOpen()
        {
            Parent.AddClient(this);
            new Thread(SendLoop)
            {
                IsBackground = true,
            }.Start();
        }

        protected override void OnClose(CloseEventArgs e)
        {
            Dispose();
            Parent.RemoveClient(this);
        }

        protected override void OnError(ErrorEventArgs e)
        {
            if (Parent.Started)
            {
                Parent.Log("WebSocket request failed: " + e.Message);
            }
        }

        public void Queue(string json)
        {
            lock (syncLock)
            {
                if (Closed)
                {
                    return;
                }

                QueuedMessages.Enqueue(json);
                Signal.Set();
            }
        }

        private void SendLoop()
        {
            try
            {
                while (true)
                {
                    Signal.WaitOne();
                    string json;
                    lock (syncLock)
                    {
                        if (Closed)
                        {
                            return;
                        }

                        if (QueuedMessages.Count == 0)
                        {
                            Signal.Reset();
                            continue;
                        }

                        json = QueuedMessages.Dequeue();
                        if (QueuedMessages.Count == 0)
                        {
                            Signal.Reset();
                        }
                    }

                    if (ReadyState != WebSocketState.Open)
                    {
                        return;
                    }

                    Send(json);
                }
            }
            catch (Exception)
            {
                // may disconnect
            }
            finally
            {
                Dispose();
                Signal.Dispose();
                Parent.RemoveClient(this);
                try
                {
                    if (ReadyState == WebSocketState.Open)
                    {
                        CloseAsync();
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        public void Dispose()
        {
            lock (syncLock)
            {
                if (Closed)
                {
                    return;
                }

                Closed = true;
                QueuedMessages.Clear();
                Signal.Set();
            }
        }
    }
}