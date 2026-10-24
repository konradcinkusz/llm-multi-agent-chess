using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Chess.Api.Tests;

public class SkeletonTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task AliveAnswersOkWithInMemoryDatabase()
    {
        var client = factory.WithWebHostBuilder(b => b.UseSetting("Database:Provider", "InMemory"))
            .CreateClient();

        var response = await client.GetAsync("/alive");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
