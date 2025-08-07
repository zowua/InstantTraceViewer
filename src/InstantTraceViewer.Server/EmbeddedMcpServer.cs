using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using InstantTraceViewer.Server.Services;
using InstantTraceViewer;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using ModelContextProtocol.Server;

namespace InstantTraceViewer.Server
{
    public class EmbeddedMcpServer : IEmbeddedMcpServer
    {
        private readonly WebApplication _app;
        private readonly TraceManager _traceManager;
        private bool _isDisposed;
        private readonly int _port;

        public EmbeddedMcpServer(int port = 15496)
        {
            _port = port;
            
            var logFile = Path.Combine(Path.GetTempPath(), "InstantTraceViewer_MCP_Debug.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(logFile, $"[{timestamp}] EmbeddedMcpServer: Constructor called with port {port}\n");
            
            var builder = WebApplication.CreateBuilder();
            
            // Configure the server to listen on the specified port
            builder.WebHost.UseUrls($"http://localhost:{port}");
            
            // Configure services
            builder.Services.AddSingleton<TraceManager>();
            builder.Services.AddSingleton<SimpleTraceFilter>();
            builder.Services.AddSingleton<TraceQueryEngine>();
            builder.Services.AddMcpServer()
                .WithHttpTransport()
                .WithToolsFromAssembly();
                
            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            _app = builder.Build();
            
            // Map MCP endpoints
            _app.MapMcp();
            
            _traceManager = _app.Services.GetRequiredService<TraceManager>();
            File.AppendAllText(logFile, $"[{timestamp}] EmbeddedMcpServer: TraceManager obtained: {_traceManager?.GetType().Name ?? "null"}\n");
        }

        public EmbeddedMcpServer() : this(15496)
        {
        }

        public async Task StartAsync()
        {
            var logFile = Path.Combine(Path.GetTempPath(), "InstantTraceViewer_MCP_Debug.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(logFile, $"[{timestamp}] EmbeddedMcpServer: StartAsync called\n");
            await _app.StartAsync();
            File.AppendAllText(logFile, $"[{timestamp}] EmbeddedMcpServer: StartAsync completed\n");
        }

        public async Task StopAsync()
        {
            await _app.StopAsync();
        }

        public void AddTraceSource(ITraceSource traceSource)
        {
            var logFile = Path.Combine(Path.GetTempPath(), "InstantTraceViewer_MCP_Debug.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(logFile, $"[{timestamp}] EmbeddedMcpServer: AddTraceSource called with: {traceSource?.DisplayName ?? "null"}\n");
            _traceManager.AddTraceSource(traceSource);
        }

        public void RemoveTraceSource(ITraceSource traceSource)
        {
            var logFile = Path.Combine(Path.GetTempPath(), "InstantTraceViewer_MCP_Debug.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(logFile, $"[{timestamp}] EmbeddedMcpServer: RemoveTraceSource called with: {traceSource?.DisplayName ?? "null"}\n");
            _traceManager.RemoveTraceSource(traceSource);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _app?.DisposeAsync().GetAwaiter().GetResult();
                }
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
