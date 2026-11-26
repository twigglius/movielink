using Dapper;
using MovieLink.Core;
using MovieLink.Core.Dtos;

namespace MovieLink.Data;

/// <summary>Read side of the movie graph for one dataset version: names, search and credit checks.</summary>
public sealed class GraphRepository
{
    private readonly Db _db;
    public GraphRepository(Db db) => _db = db;

    public async Task<bool> HasCreditAsync(int version, long actorId, long movieId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM credits WHERE dataset_version = @version AND actor_id = @actorId AND movie_id = @movieId)",
            new { version, actorId, movieId });
    }

    /// <summary>Loads exactly the credits needed to check a chain, so validation runs in memory.</summary>
    public async Task<CreditSet> CreditsForAsync(int version, IEnumerable<long> movieIds, CancellationToken ct = default)
    {
        var ids = movieIds.Distinct().ToArray();
        var set = new CreditSet();
        if (ids.Length == 0) return set;
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<(long, long)>(
            "SELECT actor_id, movie_id FROM credits WHERE dataset_version = @version AND movie_id = ANY(@ids)",
            new { version, ids });
        foreach (var (a, m) in rows) set.Add(a, m);
        return set;
    }

    public async Task<List<NodeDto>> DescribeAsync(int version, IReadOnlyList<NodeRef> nodes, CancellationToken ct = default)
    {
        var actorIds = nodes.Where(n => n.Kind == NodeKind.Actor).Select(n => n.Id).Distinct().ToArray();
        var movieIds = nodes.Where(n => n.Kind == NodeKind.Movie).Select(n => n.Id).Distinct().ToArray();
        await using var conn = await _db.OpenAsync(ct);
        var actors = (await conn.QueryAsync<(long id, string name, int? birth_year)>(
            "SELECT id, name, birth_year FROM actors WHERE dataset_version = @version AND id = ANY(@actorIds)",
            new { version, actorIds })).ToDictionary(a => a.id);
        var movies = (await conn.QueryAsync<(long id, string title, int? year)>(
            "SELECT id, title, year FROM movies WHERE dataset_version = @version AND id = ANY(@movieIds)",
            new { version, movieIds })).ToDictionary(m => m.id);

        return nodes.Select(n =>
        {
            if (n.Kind == NodeKind.Actor && actors.TryGetValue(n.Id, out var a))
                return ActorDto(a.id, a.name, a.birth_year);
            if (n.Kind == NodeKind.Movie && movies.TryGetValue(n.Id, out var m))
                return MovieDto(m.id, m.title, m.year);
            return new NodeDto { Kind = n.Kind, Id = n.Id, Name = "Unknown" };
        }).ToList();
    }

    /// <summary>
    /// Autocomplete over the whole dataset version (never filtered by the current chain, which would give
    /// the answer away). Prefix matches rank first, then trigram similarity, then popularity.
    /// </summary>
    public async Task<List<NodeDto>> SearchAsync(int version, NodeKind kind, string query, int limit = 10, CancellationToken ct = default)
    {
        string q = TextNormalizer.ForSearch(query);
        if (q.Length < 2) return new List<NodeDto>();
        string prefix = q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        string wordPrefix = "% " + prefix;
        await using var conn = await _db.OpenAsync(ct);

        if (kind == NodeKind.Actor)
        {
            var rows = await conn.QueryAsync<(long id, string name, int? birth_year)>(@"
                SELECT id, name, birth_year FROM actors
                WHERE dataset_version = @version
                  AND (search_name LIKE @prefix OR search_name LIKE @wordPrefix OR search_name % @q)
                ORDER BY (search_name LIKE @prefix) DESC, (search_name LIKE @wordPrefix) DESC,
                         similarity(search_name, @q) DESC, popularity DESC
                LIMIT @limit", new { version, q, prefix, wordPrefix, limit });
            return rows.Select(r => ActorDto(r.id, r.name, r.birth_year)).ToList();
        }
        else
        {
            var rows = await conn.QueryAsync<(long id, string title, int? year)>(@"
                SELECT id, title, year FROM movies
                WHERE dataset_version = @version
                  AND (search_title LIKE @prefix OR search_title LIKE @wordPrefix OR search_title % @q)
                ORDER BY (search_title LIKE @prefix) DESC, (search_title LIKE @wordPrefix) DESC,
                         similarity(search_title, @q) DESC, popularity DESC
                LIMIT @limit", new { version, q, prefix, wordPrefix, limit });
            return rows.Select(r => MovieDto(r.id, r.title, r.year)).ToList();
        }
    }

    private static NodeDto ActorDto(long id, string name, int? birthYear) => new()
    {
        Kind = NodeKind.Actor, Id = id, Name = name, Subtitle = birthYear is int y ? $"b. {y}" : null,
    };

    private static NodeDto MovieDto(long id, string title, int? year) => new()
    {
        Kind = NodeKind.Movie, Id = id, Name = title, Subtitle = year?.ToString(),
    };
}
