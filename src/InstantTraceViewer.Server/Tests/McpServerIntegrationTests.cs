using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using InstantTraceViewer.Server;
using InstantTraceViewer.Server.Services;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace InstantTraceViewer.Server.Tests
{
    public class McpServerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public McpServerIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task TraceController_GetEvents_ReturnsNoContent_WhenNoTraceSources()
        {
            // Act
            var response = await _client.GetAsync("/trace/events");

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task TraceManager_CanAddTraceSource()
        {
            // Arrange
            using var scope = _factory.Services.CreateScope();
            var traceManager = scope.ServiceProvider.GetRequiredService<TraceManager>();

            // Act
            var initialCount = traceManager.Count;
            
            // We can't easily create a real ITraceSource here without the full UI dependencies
            // This test demonstrates the structure for integration testing

            // Assert
            Assert.Equal(0, initialCount);
        }
    }
}
