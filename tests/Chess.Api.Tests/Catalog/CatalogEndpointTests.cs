using System.Net;
using System.Net.Http.Json;
using Chess.Api.Contracts;
using Xunit;

namespace Chess.Api.Tests.Catalog;

public sealed class FakeGatewayEndpointTests(FakeGatewayApp app) : IClassFixture<FakeGatewayApp>
{
    [Fact]
    public async Task ModelsListTheFakeModelsThenRandomWithoutWarning()
    {
        var response = await app.CreateClient().GetAsync("/api/models");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Warning"));
        var models = await response.Content.ReadFromJsonAsync<List<ModelDto>>();
        Assert.Equal(["fake-a", "fake-b", "random"], models!.Select(m => m.Id));
    }

    [Fact]
    public async Task StatusReportsTheFakeGatewayAsHealthy()
    {
        var response = await app.CreateClient().GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<StatusDto>();
        Assert.Equal("healthy", status!.Copilot);
        Assert.Equal("Fake gateway: no Copilot runtime", status.CopilotDetail);
    }
}

public sealed class NotAuthenticatedEndpointTests(NotAuthenticatedApp app) : IClassFixture<NotAuthenticatedApp>
{
    [Fact]
    public async Task ModelsFallBackToRandomOnlyWithAWarningHeader()
    {
        var response = await app.CreateClient().GetAsync("/api/models");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Warning"));
        Assert.Contains("NotAuthenticated", response.Headers.GetValues("Warning").Single());
        var models = await response.Content.ReadFromJsonAsync<List<ModelDto>>();
        Assert.Equal(["random"], models!.Select(m => m.Id));
    }

    [Fact]
    public async Task StatusReportsTheMissingSignInAsDegradedWithItsDetail()
    {
        var response = await app.CreateClient().GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<StatusDto>();
        Assert.Equal("degraded", status!.Copilot);
        Assert.Equal(NotAuthenticatedApp.Detail, status.CopilotDetail);
    }
}
