using System.Text.Json;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Benchmark;
using Xunit;

namespace Chess.Web.Tests.Benchmark;

public class BenchmarkChoicesTests
{
    private static readonly IReadOnlyList<ModelDto> Models = JsonSerializer
        .Deserialize<List<ModelDto>>(Fixtures.Models, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void CandidatesAreTheEnabledModelsExceptTheRandomPlayer()
    {
        var ids = BenchmarkChoices.Candidates(Models).Select(model => model.Id).ToArray();

        Assert.Equal(new[] { "fake-a", "fake-b", "gpt", "local", "auto" }, ids);
    }

    [Fact]
    public void OpponentsExcludeTheCandidatesModelAutoAndDisabledModelsButKeepRandom()
    {
        var ids = BenchmarkChoices.Opponents(Models, "fake-a").Select(model => model.Id).ToArray();

        Assert.Equal(new[] { "fake-b", "gpt", "local", "random" }, ids);
    }

    [Fact]
    public void AVariantIsTheModelAndItsEffortAndAModelWithoutEffortIsItsIdAlone()
    {
        Assert.Equal("fake-a@high", BenchmarkChoices.Variant("fake-a", "high"));
        Assert.Equal("gpt", BenchmarkChoices.Variant("gpt", null));
        Assert.Equal("gpt", BenchmarkChoices.Variant("gpt", ""));
    }

    [Fact]
    public void TheDefaultEffortIsTheModelsDefaultOrItsFirstListedEffort()
    {
        var fake = Models.Single(model => model.Id == "fake-a");
        Assert.Equal("medium", BenchmarkChoices.DefaultEffort(fake));
        Assert.Null(BenchmarkChoices.DefaultEffort(Models.Single(model => model.Id == "gpt")));

        var noDefault = fake with { DefaultReasoningEffort = null };
        Assert.Equal("low", BenchmarkChoices.DefaultEffort(noDefault));
    }

    [Fact]
    public void TheModelOfAPlayerIdDropsTheEffort()
    {
        Assert.Equal("fake-a", BenchmarkChoices.ModelOf("fake-a@high"));
        Assert.Equal("gpt", BenchmarkChoices.ModelOf("gpt"));
    }

    [Fact]
    public void ACustomModelIsLabelledAsSo()
    {
        Assert.Equal("My model (custom)", BenchmarkChoices.Label(Models.Single(model => model.Id == "local")));
        Assert.Equal("GPT", BenchmarkChoices.Label(Models.Single(model => model.Id == "gpt")));
    }
}
