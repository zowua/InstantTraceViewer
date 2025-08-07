using InstantTraceViewer;
using System.IO;
using System;

namespace InstantTraceViewer.Server
{
    /// <summary>
    /// Factory implementation for creating embedded MCP servers
    /// </summary>
    public class EmbeddedMcpServerFactory
    {
        public static IEmbeddedMcpServer CreateServer(int port = 15496)
        {
            var logFile = Path.Combine(Path.GetTempPath(), "InstantTraceViewer_MCP_Debug.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(logFile, $"[{timestamp}] EmbeddedMcpServerFactory: CreateServer called with port {port}\n");
            return new EmbeddedMcpServer(port);
        }

        public static IEmbeddedMcpServer CreateServer()
        {
            var logFile = Path.Combine(Path.GetTempPath(), "InstantTraceViewer_MCP_Debug.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(logFile, $"[{timestamp}] EmbeddedMcpServerFactory: CreateServer called with default port\n");
            return new EmbeddedMcpServer();
        }
    }
}
