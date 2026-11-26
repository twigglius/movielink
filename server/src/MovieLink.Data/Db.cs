using System.Reflection;
using Dapper;
using Npgsql;

namespace MovieLink.Data;

public sealed class Db
{
    private readonly NpgsqlDataSource _dataSource;

    public Db(string connectionString)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public Db(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public NpgsqlDataSource DataSource => _dataSource;

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default) =>
        await _dataSource.OpenConnectionAsync(ct);

    /// <summary>Applies embedded Migrations/*.sql in name order, once each.</summary>
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync("CREATE TABLE IF NOT EXISTS schema_migrations (name TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT now())");
        var applied = (await conn.QueryAsync<string>("SELECT name FROM schema_migrations")).ToHashSet();

        var asm = Assembly.GetExecutingAssembly();
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.EndsWith(".sql")).OrderBy(n => n, StringComparer.Ordinal))
        {
            string name = res.Split('.')[^2];
            if (applied.Contains(name)) continue;
            await using var stream = asm.GetManifestResourceStream(res)!;
            using var reader = new StreamReader(stream);
            string sql = await reader.ReadToEndAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            await conn.ExecuteAsync(sql, transaction: tx);
            await conn.ExecuteAsync("INSERT INTO schema_migrations (name) VALUES (@name)", new { name }, tx);
            await tx.CommitAsync(ct);
        }
    }
}
