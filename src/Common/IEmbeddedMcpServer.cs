using System;
using System.Threading.Tasks;

namespace InstantTraceViewer
{
    /// <summary>
    /// Interface for an embedded MCP server that can be managed by the UI
    /// </summary>
    public interface IEmbeddedMcpServer : IDisposable
    {
        Task StartAsync();
        Task StopAsync();
        void AddTraceSource(ITraceSource traceSource);
        void RemoveTraceSource(ITraceSource traceSource);
    }
}
