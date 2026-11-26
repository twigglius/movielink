namespace MovieLink.Data;

/// <summary>
/// Source-neutral dataset file. Every importer (Wikidata now, TMDB later) converts to this shape,
/// and the ingest step loads it into Postgres as a new dataset version.
/// </summary>
public sealed class DatasetFile
{
    public string Source { get; set; } = "unknown";
    public string? Notes { get; set; }
    public List<ActorRow> Actors { get; set; } = new();
    public List<MovieRow> Movies { get; set; } = new();
    public List<CreditRow> Credits { get; set; } = new();
}

public sealed class ActorRow
{
    public long Id { get; set; }
    public string? WikidataId { get; set; }
    public string Name { get; set; } = "";
    public int? BirthYear { get; set; }
    public double Popularity { get; set; }
}

public sealed class MovieRow
{
    public long Id { get; set; }
    public string? WikidataId { get; set; }
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public double Popularity { get; set; }
}

public sealed class CreditRow
{
    public long ActorId { get; set; }
    public long MovieId { get; set; }
    public int? BillingOrder { get; set; }
    public string? CharacterName { get; set; }
}
