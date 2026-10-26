using System.Net;
using System.Net.Http.Json;
using Chess.Api.Contracts;
using Chess.Api.Requests;
using Xunit;

namespace Chess.Api.Tests.Requests;

public class RequestEndpointsTests
{
    [Fact]
    public async Task The_menu_lists_the_fake_models_with_their_tiers_and_styles()
    {
        using var app = new RequestApp();
        using var client = app.CreateClient();

        var menu = await client.GetFromJsonAsync<RequestMenuDto>("/api/requests/menu");

        Assert.NotNull(menu);
        Assert.Equal(new[] { "fa", "fb", "fah" }, menu.Items.Select(item => item.Code));
        Assert.All(menu.Items, item => Assert.True(item.EstimatedUsdPerSide > 0));
        Assert.Equal(new[] { "classic", "board", "think" }, menu.Styles);
        Assert.Equal(new double[] { 1, 2, 5, 10, 20, 50 }, menu.TiersUsd);
        Assert.Equal(0.30, menu.FeeShare);
    }

    [Fact]
    public async Task A_quote_names_its_price_and_a_bad_code_is_a_bad_request()
    {
        using var app = new RequestApp();
        using var client = app.CreateClient();

        var quote = await client.GetFromJsonAsync<QuoteDto>("/api/requests/quote?white=fa&black=fb");
        var bad = await client.GetAsync("/api/requests/quote?white=nope&black=fb");

        Assert.Equal(1.0, quote!.PriceUsd);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task The_queue_is_empty_at_the_start()
    {
        using var app = new RequestApp();
        using var client = app.CreateClient();

        var queue = await client.GetFromJsonAsync<RequestQueueDto>("/api/requests/queue");

        Assert.Null(queue!.Playing);
        Assert.Empty(queue.Queued);
        Assert.Empty(queue.Recent);
        Assert.Equal(0, queue.EstimatedWaitMinutes);
    }

    [Fact]
    public async Task Simulate_runs_the_intake_in_mock_mode()
    {
        using var app = new RequestApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/requests/simulate",
            new SimulateChatRequest("frank", "Frank", 1, "USD", "!g fa vs fb"));
        var result = await response.Content.ReadFromJsonAsync<IntakeResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1.0, result!.DepositUsd);
        Assert.Equal(RequestStatus.Queued, result.Request!.Status);
        Assert.Equal(1, result.Request.QueuePosition);
    }

    [Fact]
    public async Task Simulate_answers_403_outside_mock_mode()
    {
        using var app = new RequestApp(mode: "Off");
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/requests/simulate",
            new SimulateChatRequest("frank", "Frank", 1, "USD", "!g fa vs fb"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Simulate_refuses_an_amount_that_is_not_a_positive_number()
    {
        using var app = new RequestApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/requests/simulate",
            new SimulateChatRequest("frank", "Frank", -2, "USD", "!g fa vs fb"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cancelling_an_unknown_request_is_not_found()
    {
        using var app = new RequestApp();
        using var client = app.CreateClient();

        var response = await client.PostAsync($"/api/requests/{Guid.NewGuid()}/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
