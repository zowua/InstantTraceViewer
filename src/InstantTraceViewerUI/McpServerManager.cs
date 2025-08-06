using InstantTraceViewer;
using System;
using System.Threading.Tasks;

namespace InstantTraceViewerUI
{
    public class McpServerManager : IDisposable
    {
        private IEmbeddedMcpServer? _mcpServer;
        private bool _isDisposed;

        public async Task StartServerAsync(int port = 5000)
        {
            if (_mcpServer != null)
            {
                return; // Already started
            }

            _mcpServer = McpServerFactory.CreateServer(port);
            await _mcpServer.StartAsync();
        }

        public async Task StopServerAsync()
        {
            if (_mcpServer != null)
            {
                await _mcpServer.StopAsync();
                _mcpServer.Dispose();
                _mcpServer = null;
            }
        }

        public void RegisterTraceSource(ITraceSource traceSource)
        {
            _mcpServer?.AddTraceSource(traceSource);
        }

        public void UnregisterTraceSource(ITraceSource traceSource)
        {
            _mcpServer?.RemoveTraceSource(traceSource);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    Task.Run(async () => await StopServerAsync()).Wait();
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
