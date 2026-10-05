using EmbedIO;
using EmbedIO.Actions;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HttpServer = EmbedIO.WebServer;

namespace LiveHelper.Overlay
{
    public class WebServer(ushort port, string html, Action<string> log) : IDisposable
    {
        private byte[] HtmlBody { get; set; } = Encoding.UTF8.GetBytes(html);

        private HttpServer? HttpServer { get; set; }

        private CancellationTokenSource? CancellationToken { get; set; }

        public void Start()
        {
            if (HttpServer != null)
            {
                return;
            }

            var server = new HttpServer(HttpListenerMode.EmbedIO, $"http://127.0.0.1:{port}/")
                .WithModule(new ActionModule(HandleRequest));
            server.OnUnhandledException = (context, exception) =>
            {
                log("HTTP request failed: " + exception.Message);
                return ExceptionHandler.EmptyResponse(context, exception);
            };
            var cancellation = new CancellationTokenSource();
            try
            {
                // RunAsync binds the listener before its first asynchronous wait.
                var running = server.RunAsync(cancellation.Token);
                if (running.IsCompleted)
                {
                    running.GetAwaiter().GetResult();
                }
                if (!server.Listener.IsListening)
                {
                    throw new InvalidOperationException("Cannot start overlay HTTP server.");
                }

                HttpServer = server;
                CancellationToken = cancellation;
                _ = ObserveRun(running, cancellation.Token);
            }
            catch
            {
                cancellation.Cancel();
                server.Dispose();
                cancellation.Dispose();
                throw;
            }
        }

        public void Stop()
        {
            var server = HttpServer;
            var cancellation = CancellationToken;
            HttpServer = null;
            CancellationToken = null;
            try
            {
                cancellation?.Cancel();
            }
            finally
            {
                server?.Dispose();
                cancellation?.Dispose();
            }
        }

        public void Dispose() => Stop();

        private Task HandleRequest(IHttpContext context)
        {
            if (context.Request.HttpVerb != HttpVerbs.Get)
            {
                context.Response.StatusCode = 405;
                context.Response.Headers["Allow"] = "GET";
                return Task.CompletedTask;
            }

            var path = context.Request.Url.AbsolutePath;
            if (path != "/" && path != "/overlay")
            {
                context.Response.StatusCode = 404;
                return Task.CompletedTask;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = HtmlBody.Length;
            return context.Response.OutputStream.WriteAsync(HtmlBody, 0, HtmlBody.Length, context.CancellationToken);
        }

        private async Task ObserveRun(Task running, CancellationToken cancellationToken)
        {
            try
            {
                await running.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    log("HTTP server failed: " + ex.Message);
                }
            }
        }
    }
}