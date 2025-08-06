using InstantTraceViewer;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("InstantTraceViewerUI")]

namespace InstantTraceViewer.Server
{
    /// <summary>
    /// Startup module for registering the MCP server factory
    /// This class automatically registers the server factory when the assembly is loaded
    /// </summary>
    public static class ServerStartup
    {
        private static bool _isRegistered = false;

        /// <summary>
        /// Static constructor automatically registers the factory when the assembly is loaded
        /// </summary>
        static ServerStartup()
        {
            RegisterMcpServerFactory();
        }

        /// <summary>
        /// Registers the MCP server factory. Called automatically by static constructor.
        /// </summary>
        public static void RegisterMcpServerFactory()
        {
            if (!_isRegistered)
            {
                McpServerFactory.RegisterServerFactory(port => EmbeddedMcpServerFactory.CreateServer(port));
                _isRegistered = true;
            }
        }
    }
}
