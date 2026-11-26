using System.Text.Json;
using Dapper;
using MovieLink.Data;
using MovieLink.Tools;

// movielink-tools: offline data jobs. Never run in the request path.
//   migrate
//   wikidata-fetch --out data/wikidata.json [--from 1970] [--to 2025] [--min-film-links 25] [--min-actor-links 15]
//   ingest --file tools/fixtures/seed-dataset.json [--max-billed 15]
//   puzzlegen --version N --tier 1 [--count 500] [--min-links 2] [--max-links 3] [--top-actors 400] [--min-paths 2] [--seed 1] [--publish]
//   coverage-sample --version N [--top 300] --out data/coverage.csv
var cmd = args.Length > 0 ? args[0] : "help";
var opts = ParseArgs(args.Skip(1).ToArray());
string conn = Environment.GetEnvironmentVariable("MOVIELINK_DB")
    ?? "Host=localhost;Database=movielink;Username=movielink;Password=movielink";
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = false };

switch (cmd)
{
    case "migrate":
    {
        await new Db(conn).MigrateAsync();
        Console.WriteLine("Migrations applied.");
        return 0;
    }
    case "wikidata-fetch":
    {
        var o = new WikidataOptions
        {
            FromYear = Int(opts, "from", 1970),
            ToYear = Int(opts, "to", DateTime.UtcNow.Year - 1),
            MinFilmSitelinks = Int(opts, "min-film-links", 25),
            MinActorSitelinks = Int(opts, "min-actor-links", 15),
        };
        string outPath = Req(opts, "out");
        var file = await WikidataImporter.FetchAsync(o, Console.Out);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(file, json));
        Console.WriteLine($"Wrote {outPath}: {file.Actors.Count} actors, {file.Movies.Count} movies, {file.Credits.Count} credits");
        return 0;
    }
    case "ingest":
    {
        var db = new Db(conn);
        await db.MigrateAsync();
        var file = JsonSerializer.Deserialize<DatasetFile>(await File.ReadAllTextAsync(Req(opts, "file")), json)!;
        var r = await new DatasetRepository(db).IngestAsync(file, new IngestOptions { MaxBilledCast = Int(opts, "max-billed", 15) });
        Console.WriteLine($"Dataset version {r.DatasetVersion}: {r.Actors} actors, {r.Movies} movies, {r.Credits} credits, {r.OverridesApplied} overrides applied");
        return 0;
    }
    case "puzzlegen":
    {
        var db = new Db(conn);
        var data = new DatasetRepository(db);
        int version = opts.ContainsKey("version") ? Int(opts, "version", 0) : (await data.LatestVersionAsync() ?? throw new Exception("No dataset ingested yet."));
        int tier = Int(opts, "tier", 1);
        var o = new PuzzleGenOptions
        {
            Count = Int(opts, "count", 500),
            MinLinks = Int(opts, "min-links", 2),
            MaxLinks = Int(opts, "max-links", 3),
            TopActors = Int(opts, "top-actors", 400),
            MinShortestPaths = Int(opts, "min-paths", 2),
            MaxUsesPerActor = Int(opts, "max-uses", 6),
            Seed = Int(opts, "seed", 1),
        };
        var graph = new MovieGraph(await data.LoadCreditsAsync(version));
        var pool = PuzzleGenerator.Generate(graph, await data.LoadActorPopularityAsync(version), o);
        var puzzles = new PuzzleRepository(db);
        await puzzles.WritePoolAsync(version, tier, pool);
        Console.WriteLine($"Graph v{version}: {graph.ActorCount} actors, {graph.MovieCount} movies.");
        Console.WriteLine($"Wrote {pool.Count} puzzles to tier {tier} " +
            string.Join(", ", pool.GroupBy(p => p.OptimalLinks).OrderBy(g => g.Key).Select(g => $"{g.Count()} at {g.Key} links")));
        if (pool.Count < o.Count) Console.WriteLine($"Note: asked for {o.Count}; the graph only supports {pool.Count} with these filters.");
        if (opts.ContainsKey("publish"))
        {
            await puzzles.PublishPoolAsync(version, tier);
            Console.WriteLine($"Published tier {tier} from dataset v{version} (dataset is now frozen).");
        }
        return 0;
    }
    case "coverage-sample":
    {
        // Phase 1 check: export the top films with their cast so a reviewer can compare against on-screen credits.
        var db = new Db(conn);
        int version = Int(opts, "version", 0);
        int top = Int(opts, "top", 300);
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<(string title, int? year, string? qid, string cast)>(@"
            SELECT m.title, m.year, m.wikidata_id,
                   string_agg(a.name, '; ' ORDER BY coalesce(cr.billing_order, 999), a.name) AS cast
            FROM (SELECT * FROM movies WHERE dataset_version = @version ORDER BY popularity DESC LIMIT @top) m
            JOIN credits cr ON cr.dataset_version = m.dataset_version AND cr.movie_id = m.id
            JOIN actors a ON a.dataset_version = cr.dataset_version AND a.id = cr.actor_id
            GROUP BY m.title, m.year, m.wikidata_id, m.popularity ORDER BY m.popularity DESC", new { version, top });
        string outPath = Req(opts, "out");
        await using var w = new StreamWriter(outPath);
        await w.WriteLineAsync("title,year,wikidata_id,cast_in_dataset,missing_leads (fill in),notes");
        foreach (var r in rows)
            await w.WriteLineAsync($"{Csv(r.title)},{r.year},{r.qid},{Csv(r.cast)},,");
        Console.WriteLine($"Wrote {outPath}");
        return 0;
    }
    default:
        Console.WriteLine("Commands: migrate | wikidata-fetch | ingest | puzzlegen | coverage-sample. See Program.cs header.");
        return cmd == "help" ? 0 : 1;
}

static Dictionary<string, string> ParseArgs(string[] a)
{
    var d = new Dictionary<string, string>();
    for (int i = 0; i < a.Length; i++)
    {
        if (!a[i].StartsWith("--")) continue;
        string k = a[i][2..];
        d[k] = i + 1 < a.Length && !a[i + 1].StartsWith("--") ? a[++i] : "true";
    }
    return d;
}
static int Int(Dictionary<string, string> d, string k, int def) => d.TryGetValue(k, out var v) ? int.Parse(v) : def;
static string Req(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : throw new ArgumentException($"--{k} is required");
static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
