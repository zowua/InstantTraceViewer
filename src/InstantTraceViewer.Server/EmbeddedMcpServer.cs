using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using InstantTraceViewer.Server.Services;
using InstantTraceViewer;
using System;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace InstantTraceViewer.Server
{
    public class EmbeddedMcpServer : IEmbeddedMcpServer
    {
        private readonly IHost _host;
        private readonly TraceManager _traceManager;
        private bool _isDisposed;

        public EmbeddedMcpServer(int port = 5000)
        {
            var builder = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<TraceManager>();
                    services.AddSingleton<SimpleTraceFilter>();
                    services.AddMcpServer()
                        .WithStdioServerTransport()
                        .WithToolsFromAssembly();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Warning);
                });

            _host = builder.Build();
            _traceManager = _host.Services.GetRequiredService<TraceManager>();
        }

        public async Task StartAsync()
        {
            await _host.StartAsync();
        }

        public async Task StopAsync()
        {
            await _host.StopAsync();
        }

        public void AddTraceSource(ITraceSource traceSource)
        {
            _traceManager.AddTraceSource(traceSource);
        }

        public void RemoveTraceSource(ITraceSource traceSource)
        {
            _traceManager.RemoveTraceSource(traceSource);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _host?.Dispose();
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
