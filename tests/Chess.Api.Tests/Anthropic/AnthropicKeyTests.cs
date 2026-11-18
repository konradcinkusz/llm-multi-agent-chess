using System.Net;
using System.Net.Http.Json;
using System.Text;
using Chess.Api.Anthropic;
using Chess.Api.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Chess.Api.Tests.Anthropic;

/// <summary>A key typed into the app is checked with Anthropic, kept off the repository, and unlocks every model the key lists.</summary>
public sealed class AnthropicKeyTests : IDisposable
{
    private const string GoodKey = "sk-ant-test-good-key";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "arena-anthropic-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Parse_reads_ids_display_names_and_paging()
    {
        using var json = System.Text.Json.JsonDocument.Parse(
            """{"data":[{"id":"claude-a","display_name":"Claude A","type":"model"},{"id":"claude-b","type":"model"}],"has_more":true,"last_id":"claude-b"}""");

        var (models, hasMore, lastId) = AnthropicModelDiscovery.Parse(json.RootElement);

        Assert.Equal([new AnthropicModel("claude-a", "Claude A"), new AnthropicModel("claude-b", "claude-b")], models);
        Assert.True(hasMore);
        Assert.Equal("claude-b", lastId);
    }

    [Fact]
    public void The_key_is_stored_encrypted_and_read_back()
    {
        var store = NewStore();

        store.Save(GoodKey);

        Assert.Equal(GoodKey, store.Load());
        Assert.DoesNotContain(GoodKey, File.ReadAllText(Path.Combine(_folder, "anthropic.key")));
        store.Delete();
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task A_good_key_is_saved_and_its_models_are_listed_with_the_others()
    {
        using var app = NewApp(new AnthropicStub());
        using var client = app.CreateClient();

        var saved = await client.PutAsJsonAsync("/api/anthropic", new { apiKey = GoodKey });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await saved.Content.ReadAsStringAsync();
        Assert.DoesNotContain(GoodKey, body);
        var status = await client.GetFromJsonAsync<AnthropicStatusDto>("/api/anthropic");
        Assert.Equal(new AnthropicStatusDto(true, 2, status!.RefreshedAt, null), status);

        var models = await client.GetFromJsonAsync<ModelDto[]>("/api/models");
        var listed = Assert.Single(models!, m => m.Id == "claude-opus-x");
        Assert.True(listed.Custom);
        Assert.Contains("Anthropic API", listed.DisplayName);
        Assert.Contains(models!, m => m.Id == "claude-haiku-x");
    }

    [Fact]
    public async Task A_refused_key_is_not_saved()
    {
        using var app = NewApp(new AnthropicStub());
        using var client = app.CreateClient();

        var response = await client.PutAsJsonAsync("/api/anthropic", new { apiKey = "sk-ant-wrong" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(NewStore().Load());
        Assert.False((await client.GetFromJsonAsync<AnthropicStatusDto>("/api/anthropic"))!.Configured);
        Assert.DoesNotContain(await client.GetFromJsonAsync<ModelDto[]>("/api/models") ?? [], m => m.Custom);
    }

    [Fact]
    public async Task An_empty_key_is_a_bad_request()
    {
        using var app = NewApp(new AnthropicStub());
        using var client = app.CreateClient();

        var response = await client.PutAsJsonAsync("/api/anthropic", new { apiKey = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Removing_the_key_takes_the_models_off_the_list()
    {
        using var app = NewApp(new AnthropicStub());
        using var client = app.CreateClient();
        await client.PutAsJsonAsync("/api/anthropic", new { apiKey = GoodKey });

        var removed = await client.DeleteAsync("/api/anthropic");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.False((await client.GetFromJsonAsync<AnthropicStatusDto>("/api/anthropic"))!.Configured);
        Assert.DoesNotContain(await client.GetFromJsonAsync<ModelDto[]>("/api/models") ?? [], m => m.Custom);
        Assert.Null(NewStore().Load());
    }

    [Fact]
    public async Task Refresh_without_a_key_is_a_conflict()
    {
        using var app = NewApp(new AnthropicStub());
        using var client = app.CreateClient();

        var response = await client.PostAsync("/api/anthropic/refresh", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_saved_key_is_loaded_again_when_the_app_starts()
    {
        NewStore().Save(GoodKey);
        using var app = NewApp(new AnthropicStub());
        using var client = app.CreateClient();

        var status = await WaitForAsync(client, s => s.ModelCount > 0);

        Assert.True(status.Configured);
        Assert.Equal(2, status.ModelCount);
    }

    private AnthropicKeyStore NewStore() =>
        new(Microsoft.Extensions.Options.Options.Create(new AnthropicOptions { KeyFolder = _folder }));

    private WebApplicationFactory<Program> NewApp(HttpMessageHandler anthropic) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Provider", "InMemory");
            builder.UseSetting("Llm:Provider", "Fake");
            builder.UseSetting("Arena:Anthropic:KeyFolder", _folder);
            builder.ConfigureServices(services =>
                services.AddHttpClient(AnthropicModelDiscovery.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => anthropic));
        });

    private static async Task<AnthropicStatusDto> WaitForAsync(HttpClient client, Func<AnthropicStatusDto, bool> done)
    {
        for (var i = 0; i < 100; i++)
        {
            var status = (await client.GetFromJsonAsync<AnthropicStatusDto>("/api/anthropic"))!;
            if (done(status))
            {
                return status;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("The Anthropic status did not change.");
    }

    /// <summary>Stands in for api.anthropic.com: one good key, two models.</summary>
    private sealed class AnthropicStub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.Headers.TryGetValues("x-api-key", out var values) ? values.Single() : null;
            if (key != GoodKey)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            const string json = """{"data":[{"id":"claude-opus-x","display_name":"Claude Opus X"},{"id":"claude-haiku-x","display_name":"Claude Haiku X"}],"has_more":false}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
