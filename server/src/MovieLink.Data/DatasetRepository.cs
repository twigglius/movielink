using Dapper;
using MovieLink.Core;
using Npgsql;
using NpgsqlTypes;

namespace MovieLink.Data;

public sealed class IngestOptions
{
    /// <summary>Keep only the top N billed cast per film when billing order is known.</summary>
    public int MaxBilledCast { get; set; } = 15;
}

public sealed class IngestResult
{
    public int DatasetVersion { get; set; }
    public int Actors { get; set; }
    public int Movies { get; set; }
    public int Credits { get; set; }
    public int OverridesApplied { get; set; }
}

public sealed class DatasetRepository
{
    private readonly Db _db;
    public DatasetRepository(Db db) => _db = db;

    /// <summary>Loads a dataset file as a brand-new dataset version. Never mutates an existing version.</summary>
    public async Task<IngestResult> IngestAsync(DatasetFile file, IngestOptions? options = null, CancellationToken ct = default)
    {
        options ??= new IngestOptions();
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        int version = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO dataset_versions (source, notes) VALUES (@Source, @Notes) RETURNING id", file, tx);

        var actorIds = file.Actors.Select(a => a.Id).ToHashSet();
        var movieIds = file.Movies.Select(m => m.Id).ToHashSet();

        var credits = file.Credits
            .Where(c => actorIds.Contains(c.ActorId) && movieIds.Contains(c.MovieId))
            .Where(c => c.BillingOrder is null || c.BillingOrder <= options.MaxBilledCast)
            .GroupBy(c => (c.ActorId, c.MovieId))
            .Select(g => g.OrderBy(c => c.BillingOrder ?? int.MaxValue).First())
            .ToList();

        // Apply hand overrides (source-neutral fixes owned by us).
        var overrides = (await conn.QueryAsync<(long actor_id, long movie_id, string action)>(
            "SELECT actor_id, movie_id, action FROM credits_overrides", transaction: tx)).ToList();
        int applied = 0;
        foreach (var o in overrides)
        {
            if (o.action == "remove")
                applied += credits.RemoveAll(c => c.ActorId == o.actor_id && c.MovieId == o.movie_id);
            else if (actorIds.Contains(o.actor_id) && movieIds.Contains(o.movie_id)
                     && !credits.Any(c => c.ActorId == o.actor_id && c.MovieId == o.movie_id))
            {
                credits.Add(new CreditRow { ActorId = o.actor_id, MovieId = o.movie_id });
                applied++;
            }
        }

        await using (var w = await conn.BeginBinaryImportAsync(
            "COPY actors (dataset_version, id, wikidata_id, name, search_name, birth_year, popularity) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var a in file.Actors.DistinctBy(a => a.Id))
            {
                await w.StartRowAsync(ct);
                await w.WriteAsync(version, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(a.Id, NpgsqlDbType.Bigint, ct);
                await WriteNullable(w, a.WikidataId, NpgsqlDbType.Text, ct);
                await w.WriteAsync(a.Name, NpgsqlDbType.Text, ct);
                await w.WriteAsync(TextNormalizer.ForSearch(a.Name), NpgsqlDbType.Text, ct);
                await WriteNullable(w, a.BirthYear, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(a.Popularity, NpgsqlDbType.Double, ct);
            }
            await w.CompleteAsync(ct);
        }

        await using (var w = await conn.BeginBinaryImportAsync(
            "COPY movies (dataset_version, id, wikidata_id, title, search_title, year, popularity) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var m in file.Movies.DistinctBy(m => m.Id))
            {
                await w.StartRowAsync(ct);
                await w.WriteAsync(version, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(m.Id, NpgsqlDbType.Bigint, ct);
                await WriteNullable(w, m.WikidataId, NpgsqlDbType.Text, ct);
                await w.WriteAsync(m.Title, NpgsqlDbType.Text, ct);
                await w.WriteAsync(TextNormalizer.ForSearch(m.Title), NpgsqlDbType.Text, ct);
                await WriteNullable(w, m.Year, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(m.Popularity, NpgsqlDbType.Double, ct);
            }
            await w.CompleteAsync(ct);
        }

        await using (var w = await conn.BeginBinaryImportAsync(
            "COPY credits (dataset_version, actor_id, movie_id, billing_order, character_name) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var c in credits)
            {
                await w.StartRowAsync(ct);
                await w.WriteAsync(version, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(c.ActorId, NpgsqlDbType.Bigint, ct);
                await w.WriteAsync(c.MovieId, NpgsqlDbType.Bigint, ct);
                await WriteNullable(w, c.BillingOrder, NpgsqlDbType.Integer, ct);
                await WriteNullable(w, c.CharacterName, NpgsqlDbType.Text, ct);
            }
            await w.CompleteAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new IngestResult
        {
            DatasetVersion = version,
            Actors = actorIds.Count,
            Movies = movieIds.Count,
            Credits = credits.Count,
            OverridesApplied = applied,
        };
    }

    private static Task WriteNullable<T>(NpgsqlBinaryImporter w, T? value, NpgsqlDbType type, CancellationToken ct) =>
        value is null ? w.WriteNullAsync(ct) : w.WriteAsync(value, type, ct);

    /// <summary>All credits of a dataset version, for building the graph offline.</summary>
    public async Task<List<(long ActorId, long MovieId)>> LoadCreditsAsync(int version, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<(long, long)>(
            "SELECT actor_id, movie_id FROM credits WHERE dataset_version = @version", new { version });
        return rows.ToList();
    }

    public async Task<Dictionary<long, double>> LoadActorPopularityAsync(int version, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<(long id, double pop)>(
            "SELECT id, popularity FROM actors WHERE dataset_version = @version", new { version });
        return rows.ToDictionary(r => r.id, r => r.pop);
    }

    public async Task<Dictionary<long, double>> LoadMoviePopularityAsync(int version, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<(long id, double pop)>(
            "SELECT id, popularity FROM movies WHERE dataset_version = @version", new { version });
        return rows.ToDictionary(r => r.id, r => r.pop);
    }

    public async Task<int?> LatestVersionAsync(CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int?>("SELECT max(id) FROM dataset_versions");
    }
}
