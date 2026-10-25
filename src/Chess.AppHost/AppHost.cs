using Aspire.Hosting.ApplicationModel;
using Chess.AppHost;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// The fake profile (launchSettings "fake", or ARENA_FAKE=1) needs no Docker: the API runs the fake
// LLM gateway on the InMemory provider and no Postgres is started. E2E tests use it too.
var fake = builder.Configuration["ARENA_FAKE"] is "1" or "true";

var api = builder.AddProject<Projects.Chess_Api>("api")
    .WithHttpEndpoint(port: 5080, targetPort: 5080, name: "http", isProxied: false)
    .WithHttpHealthCheck("/health");

// Custom models (Arena:CustomModels, 03 §12.1): the plain fields are forwarded as environment variables; each API key is a
// secret parameter (Parameters:custom-model-<id>-key, user secrets), never a value written to a file or a log.
foreach (var setting in CustomModelSettings.Fields(builder.Configuration))
{
    api.WithEnvironment(setting.Name, setting.Value);
}

foreach (var key in CustomModelSettings.Keys(builder.Configuration))
{
    api.WithEnvironment(key.Name, builder.AddParameter(key.ParameterName, secret: true));
}

var web = builder.AddProject<Projects.Chess_Web>("web")
    .WithHttpEndpoint(port: 5070, targetPort: 5070, name: "http", isProxied: false)
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api)
    .WithHttpHealthCheck("/health");

if (fake)
{
    api.WithEnvironment("Llm__Provider", "Fake")
        .WithEnvironment("Database__Provider", "InMemory");
}
else
{
    // An existing Postgres (ConnectionStrings:chess in AppHost user secrets) replaces the container.
    // Aspire 13.6 saves a GenerateParameterDefault value to user secrets only when the AppHost has a
    // UserSecretsId (IUserSecretsManager.IsAvailable; the "PersistentContainerWithoutUserSecrets"
    // warning). Without it a new password is generated per run and the existing volume rejects it.
    var database = builder.Configuration.GetConnectionString("chess") is not null
        ? builder.AddConnectionString("chess")
        : builder.AddPostgres("postgres", password: builder.AddParameter("postgres-password", new GenerateParameterDefault { MinLength = 32 }, secret: true))
            .WithDataVolume("chess-arena-pg")
            .AddDatabase("chess");

    api.WithReference(database)
        .WaitFor(database);

    // Optional: without it the Copilot runtime uses the login already on this machine.
    if (builder.Configuration["Parameters:copilot-github-token"] is { Length: > 0 })
    {
        api.WithEnvironment("Copilot__GitHubToken", builder.AddParameter("copilot-github-token", secret: true));
    }
}

builder.Build().Run();
