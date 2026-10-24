using Chess.Llm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chess.Api.Tests.Catalog;

/// <summary>The app with the deterministic fake gateway in its <c>Fake</c> state.</summary>
public sealed class FakeGatewayApp : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "InMemory");
        builder.UseSetting("Llm:Provider", "Fake");
    }
}

/// <summary>The app with a gateway stub that reports no signed-in Copilot user.</summary>
public sealed class NotAuthenticatedApp : WebApplicationFactory<Program>
{
    public const string Detail = "No GitHub Copilot user is signed in.";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "InMemory");
        builder.UseSetting("Llm:Provider", "Fake");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILlmGateway>();
            services.AddSingleton<ILlmGateway>(new StubGateway(new LlmGatewayStatus(LlmGatewayState.NotAuthenticated, Detail)));
        });
    }
}
