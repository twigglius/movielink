using System.Text.Json.Serialization;
using MovieLink.Api;
using MovieLink.Core;
using MovieLink.Core.Dtos;
using MovieLink.Data;

var builder = WebApplication.CreateBuilder(args);

var game = builder.Configuration.GetSection("Game").Get<GameOptions>() ?? new GameOptions();
if (string.IsNullOrEmpty(game.TokenKey))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Game:TokenKey must be set outside Development.");
    game.TokenKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}

string conn = builder.Configuration.GetConnectionString("MovieLink")
    ?? "Host=localhost;Database=movielink;Username=movielink;Password=movielink";

builder.Services.AddSingleton(game);
builder.Services.AddSingleton(new Db(conn));
builder.Services.AddSingleton<PuzzleRepository>();
builder.Services.AddSingleton<MatchRepository>();
builder.Services.AddSingleton<GraphRepository>();
builder.Services.AddSingleton(new MatchTokens(game.TokenKey));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MatchService>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (game.CorsOrigins.Contains("*")) p.AllowAnyOrigin(); else p.WithOrigins(game.CorsOrigins);
    p.AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();
app.UseCors();

if (builder.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
    await app.Services.GetRequiredService<Db>().MigrateAsync();

const string TokenHeader = "X-Match-Token";

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");

api.MapPost("/matches", async (CreateMatchRequest req, MatchService svc, CancellationToken ct) =>
    await Guard(async () => Results.Ok(await svc.CreateAsync(req, ct))));

api.MapGet("/matches/{id:guid}/search", async (Guid id, string q, string kind, HttpRequest http,
    MatchService svc, GraphRepository graph, CancellationToken ct) =>
    await Guard(async () =>
    {
        if (!Enum.TryParse<NodeKind>(kind, ignoreCase: true, out var nodeKind))
            throw new ArgumentException("kind must be 'actor' or 'movie'");
        int version = await svc.DatasetVersionForAsync(id, http.Headers[TokenHeader], ct);
        var results = await graph.SearchAsync(version, nodeKind, q, 10, ct);
        return Results.Ok(new SearchResponse { Results = results });
    }));

api.MapPost("/matches/{id:guid}/steps", async (Guid id, StepRequest req, HttpRequest http, MatchService svc, CancellationToken ct) =>
    await Guard(async () => Results.Ok(await svc.StepAsync(id, http.Headers[TokenHeader], req, ct))));

api.MapPost("/matches/{id:guid}/undo", async (Guid id, HttpRequest http, MatchService svc, CancellationToken ct) =>
    await Guard(async () => Results.Ok(await svc.UndoAsync(id, http.Headers[TokenHeader], ct))));

api.MapPost("/matches/{id:guid}/submit", async (Guid id, HttpRequest http, MatchService svc, CancellationToken ct) =>
    await Guard(async () => Results.Ok(await svc.SubmitAsync(id, http.Headers[TokenHeader], ct))));

app.Run();

static async Task<IResult> Guard(Func<Task<IResult>> f)
{
    try { return await f(); }
    catch (MatchNotFoundException) { return Results.NotFound(new { error = "match_not_found" }); }
    catch (BadTokenException) { return Results.Json(new { error = "bad_token" }, statusCode: 401); }
    catch (NoPuzzlesException e) { return Results.Json(new { error = "no_puzzles", message = e.Message }, statusCode: 503); }
    catch (ArgumentException e) { return Results.BadRequest(new { error = "bad_request", message = e.Message }); }
}

public partial class Program { }
