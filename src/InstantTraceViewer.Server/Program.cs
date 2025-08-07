using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using InstantTraceViewer.Server.Services;
using InstantTraceViewer.Server.Providers;
using InstantTraceViewer;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Add MCP server with STDIO transport
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        // Add TraceManager as a singleton service
        builder.Services.AddSingleton<TraceManager>();

        // Add TraceQueryEngine as a singleton service
        builder.Services.AddSingleton<TraceQueryEngine>();

        await builder.Build().RunAsync();
    }
}
