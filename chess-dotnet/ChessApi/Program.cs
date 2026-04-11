using ChessApi.Agents;
using ChessApi.Config;
using ChessApi.Data;
using ChessApi.Hubs;
using ChessApi.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("AppConfig"));

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContextFactory<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Application services ──────────────────────────────────────────────────────
builder.Services.AddSingleton<AgentFactory>();
builder.Services.AddSingleton<IGameService, GameService>();

// ── ASP.NET Core ──────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true)
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials())); // required for SignalR

var app = builder.Build();

// ── Migrations on startup ─────────────────────────────────────────────────────
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// ── Middleware ────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.MapControllers();
app.MapHub<GameHub>("/gamehub");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
