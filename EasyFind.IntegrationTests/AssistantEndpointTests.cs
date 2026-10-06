using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace EasyFind.IntegrationTests;

public class AssistantEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    // Every chat call spends Gemini quota. Without [Authorize] (and with no
    // fallback policy in Program.cs) anyone on the internet could run up the
    // bill; without [Route] the action was mapped to a bare "/chat" and this
    // URL answered 404. A 401 here proves both: the route exists, and it is
    // closed to anonymous callers.
    [Fact]
    public async Task Chat_WithoutAuth_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/assistant/chat",
            new { messages = new[] { new { role = "user", text = "hi" } } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
