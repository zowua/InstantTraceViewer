using InstantTraceViewer;
using System;
using System.IO;
using System.Threading.Tasks;

namespace InstantTraceViewerUI
{
    public class McpServerManager : IDisposable
    {
        private IEmbeddedMcpServer? _mcpServer;
        private bool _isDisposed;

        public async Task StartServerAsync(int port = 15496)
        {
            var logFile = Path.Combine(Path.GetTempPath(), "InstantTraceViewer_MCP_Debug.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(logFile, $"[{timestamp}] McpServerManager: StartServerAsync called with port {port}\n");
            
            if (_mcpServer != null)
            {
                File.AppendAllText(logFile, $"[{timestamp}] McpServerManager: Server already started, returning\n");
                return; // Already started
            }

            File.AppendAllText(logFile, $"[{timestamp}] McpServerManager: Creating MCP server\n");
            _mcpServer = McpServerFactory.CreateServer(port);
            File.AppendAllText(logFile, $"[{timestamp}] McpServerManager: MCP server created: {_mcpServer?.GetType().Name ?? "null"}\n");
            
            await _mcpServer.StartAsync();
            File.AppendAllText(logFile, $"[{timestamp}] McpServerManager: MCP server started\n");
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
            System.Diagnostics.Debug.WriteLine($"[DEBUG] McpServerManager: Registering trace source: {traceSource?.DisplayName ?? "null"}");
            _mcpServer?.AddTraceSource(traceSource);
        }

        public void UnregisterTraceSource(ITraceSource traceSource)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] McpServerManager: Unregistering trace source: {traceSource?.DisplayName ?? "null"}");
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
