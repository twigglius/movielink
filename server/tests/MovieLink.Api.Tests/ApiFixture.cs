using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using MovieLink.Data;
using MovieLink.Tools;
using Npgsql;
using Xunit;

namespace MovieLink.Api.Tests;

/// <summary>
/// Spins up the API against a throwaway Postgres database seeded with the dev fixture.
/// Needs a Postgres server: set MOVIELINK_TEST_DB_ADMIN (a connection string with CREATEDB rights),
/// default Host=localhost;Username=movielink;Password=movielink;Database=postgres.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));
    public string ConnectionString { get; private set; } = "";
    public DatasetFile Seed { get; private set; } = new();
    public List<PuzzleRecord> Pool { get; private set; } = new();
    private string _dbName = "";
    private string _admin = "";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("MOVIELINK_TEST_DB_ADMIN")
            ?? "Host=localhost;Username=movielink;Password=movielink;Database=postgres";
        _dbName = "movielink_test_" + Guid.NewGuid().ToString("N")[..8];
        await using (var c = new NpgsqlConnection(_admin))
            await c.ExecuteAsync($"CREATE DATABASE {_dbName}");
        ConnectionString = new NpgsqlConnectionStringBuilder(_admin) { Database = _dbName }.ToString();

        var db = new Db(ConnectionString);
        await db.MigrateAsync();
        Seed = JsonSerializer.Deserialize<DatasetFile>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "seed-dataset.json")), Json)!;
        var ingest = await new DatasetRepository(db).IngestAsync(Seed);
        var data = new DatasetRepository(db);
        var graph = new MovieGraph(await data.LoadCreditsAsync(ingest.DatasetVersion));
        Pool = PuzzleGenerator.Generate(graph, await data.LoadActorPopularityAsync(ingest.DatasetVersion),
            new PuzzleGenOptions { Count = 50, TopActors = 200, MinShortestPaths = 1, Seed = 7 });
        var puzzles = new PuzzleRepository(db);
        await puzzles.WritePoolAsync(ingest.DatasetVersion, 1, Pool);
        await puzzles.PublishPoolAsync(ingest.DatasetVersion, 1);
        db.DataSource.Dispose();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:MovieLink", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Game:TokenKey", Convert.ToBase64String(new byte[32]));
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
        });
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(_admin);
        await c.ExecuteAsync($"DROP DATABASE IF EXISTS {_dbName} WITH (FORCE)");
    }
}
