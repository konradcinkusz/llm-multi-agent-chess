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

var app = builder.Build();
app.MapDefaultEndpoints();
var api = app.MapGroup("/api");
api.MapCatalogEndpoints();
api.MapGameEndpoints();
api.MapTournamentEndpoints();
api.MapRatingEndpoints();
api.MapResultEndpoints();
api.MapAttemptEndpoints();
app.Run();

public partial class Program;
