using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using WebDto = Chess.Web.Api;

namespace Chess.Web.Tests.Results;

// R1: the two arena bounds are additive on ModelRating (03 §3.4) and on its web copy. The API's JSON must round-trip with
// them, and a payload without them (an older API) must still deserialise.
public class ModelRatingContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TheArenaBoundsRoundTripThroughTheWebCopy()
    {
        var api = new Chess.Rating.ModelRating("gpt-5", 1520.5, 1490.1, 1551.0, 30, 0.62, 0.97, 0.88, 0.74, 66.0, false, 61.25, 70.5);
        var apiJson = JsonSerializer.Serialize(api, Json);

        var web = JsonSerializer.Deserialize<WebDto.ModelRating>(apiJson, Json);

        Assert.NotNull(web);
        Assert.Equal(61.25, web.ArenaScoreLow);
        Assert.Equal(70.5, web.ArenaScoreHigh);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(apiJson), JsonNode.Parse(JsonSerializer.Serialize(web, Json))),
            $"lost data: {apiJson}");
    }

    [Fact]
    public void APayloadWithoutTheBoundsStillDeserialises()
    {
        const string withoutBounds =
            """{"model":"gpt-5","rating":1520.5,"ciLow":1490.1,"ciHigh":1551,"games":30,"score":0.62,"legality":0.97,"moveQuality":null,"quality":0.74,"arenaScore":66,"provisional":false}""";

        var web = JsonSerializer.Deserialize<WebDto.ModelRating>(withoutBounds, Json);

        Assert.NotNull(web);
        Assert.Null(web.ArenaScoreLow);
        Assert.Null(web.ArenaScoreHigh);
    }
}
