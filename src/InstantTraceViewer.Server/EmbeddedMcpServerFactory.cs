using InstantTraceViewer;

namespace InstantTraceViewer.Server
{
    /// <summary>
    /// Factory implementation for creating embedded MCP servers
    /// </summary>
    public class EmbeddedMcpServerFactory
    {
        public static IEmbeddedMcpServer CreateServer(int port = 5000)
        {
            return new EmbeddedMcpServer(port);
        }
    }
}
