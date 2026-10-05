using IPA;
using IPA.Logging;
using LiveHelper.Overlay;
using System;
using System.IO;
using System.Reflection;

namespace LiveHelper
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public sealed class Plugin
    {
        public const ushort OverlayWebPort = 6735;
        public const ushort OverlayWebsocketPort = 6734;

        public static Plugin? Instance { get; private set; }

        public Logger Logger { get; private set; } = null!;

        public SessionState SessionState { get; } = new SessionState();

        public OverlayServer? OverlayServer { get; set; }

        public HookEngine? HookEngine { get; set; }

        [Init]
        public void Init(Logger logger)
        {
            Logger = logger;
            Instance = this;
        }

        [OnStart]
        public void OnStart()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LiveHelper.overlay.html"))
                using (var reader = new StreamReader(stream ?? throw new InvalidOperationException("Cannot Load Overlay Html")))
                {
                    OverlayServer = new OverlayServer(OverlayWebsocketPort, OverlayWebPort, reader.ReadToEnd(), SessionState, (message) => Logger.Warn(message));
                }

                OverlayServer.Start();
                Logger.Info("Overlay ready at http://127.0.0.1:6735/overlay (WebSocket ws://127.0.0.1:6734)");
            }
            catch (Exception ex)
            {
                Logger.Error("Cannot start overlay server: " + ex);
                OverlayServer?.Dispose();
                OverlayServer = null;
            }

            try
            {
                HookEngine = new HookEngine(this);
                HookEngine.Start();
            }
            catch (Exception ex) 
            { 
                Logger.Error("Cannot attach game events: " + ex); 
            }
        }

        [OnExit]
        public void OnExit()
        {
            HookEngine?.Dispose();
            HookEngine = null;
            SessionState.End();
            OverlayServer?.Dispose();
            OverlayServer = null;
            Instance = null;
        }
    }
}