using System;
using System.Threading.Tasks;

namespace InstantTraceViewer
{
    /// <summary>
    /// Factory for creating MCP server instances. This allows the UI to create server instances
    /// without directly depending on the server project.
    /// </summary>
    public static class McpServerFactory
    {
        private static Func<int, IEmbeddedMcpServer>? _serverFactory;

        /// <summary>
        /// Register a factory function that can create MCP server instances.
        /// This should be called by the application startup code.
        /// </summary>
        public static void RegisterServerFactory(Func<int, IEmbeddedMcpServer> factory)
        {
            _serverFactory = factory;
        }

        /// <summary>
        /// Creates a new MCP server instance, or returns null if no factory is registered.
        /// </summary>
        /// <param name="port">The port for the server to listen on</param>
        /// <returns>A new MCP server instance, or null if no factory is registered</returns>
        public static IEmbeddedMcpServer? CreateServer(int port = 5000)
        {
            return _serverFactory?.Invoke(port);
        }

        /// <summary>
        /// Gets whether a server factory has been registered
        /// </summary>
        public static bool IsFactoryRegistered => _serverFactory != null;
    }

    /// <summary>
    /// A no-op implementation of IEmbeddedMcpServer for when the server is not available
    /// </summary>
    public class NoOpMcpServer : IEmbeddedMcpServer
    {
        public Task StartAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public void AddTraceSource(ITraceSource traceSource) { }
        public void RemoveTraceSource(ITraceSource traceSource) { }
        public void Dispose() { }
    }
}
