using Chess.Api.Benchmarks;
using Chess.Api.Broadcast;
using Chess.Api.Requests;
using Chess.Api.Usage;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddArenaPersistence();
builder.AddLlmGateway();
builder.AddMoveQualityAnalysis();
builder.AddPlayers();
builder.AddGameRunner();
builder.AddTournaments();
builder.AddRatings();
builder.AddResults();
builder.AddCatalog();
builder.AddAttempts();
builder.AddMoveQualityPipeline();
builder.AddBroadcast();
builder.AddRequests();

var app = builder.Build();
app.UseOriginGuard();
app.MapDefaultEndpoints();
var api = app.MapGroup("/api");
api.MapCatalogEndpoints();
api.MapGameEndpoints();
api.MapTournamentEndpoints();
api.MapBenchmarkEndpoints();
api.MapRatingEndpoints();
api.MapResultEndpoints();
api.MapAttemptEndpoints();
api.MapUsageEndpoints();
api.MapBroadcastEndpoints();
api.MapRequestEndpoints();
app.Run();

public partial class Program;
