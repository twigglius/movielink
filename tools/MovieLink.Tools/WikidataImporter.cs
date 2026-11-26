using System.Net.Http.Headers;
using System.Text.Json;
using MovieLink.Data;

namespace MovieLink.Tools;

public sealed class WikidataOptions
{
    public int FromYear { get; set; } = 1970;
    public int ToYear { get; set; } = DateTime.UtcNow.Year;
    /// <summary>Films need at least this many Wikipedia language editions (a fame filter).</summary>
    public int MinFilmSitelinks { get; set; } = 25;
    /// <summary>Actors need at least this many sitelinks to be kept.</summary>
    public int MinActorSitelinks { get; set; } = 15;
    /// <summary>Wikidata asks bots to identify themselves. Set WIKIDATA_USER_AGENT to include your contact.</summary>
    public string UserAgent { get; set; } =
        Environment.GetEnvironmentVariable("WIKIDATA_USER_AGENT") ?? "MovieLinkIngest/0.1 (dataset builder)";
    public string Endpoint { get; set; } = "https://query.wikidata.org/sparql";
}

/// <summary>
/// Builds a DatasetFile from Wikidata (CC0). Uses the SPARQL endpoint one year at a time, which is
/// fine for the scoped graph (famous films only). For the full catalogue, switch to the weekly JSON dump.
/// Popularity = sitelink count for now; Wikipedia page views are the planned upgrade.
/// </summary>
public static class WikidataImporter
{
    // Film (Q11424) and feature film (Q24869). Animated films are left out on purpose: voice roles are
    // excluded at launch. Cast is P161 with optional series ordinal P1545 for billing order.
    internal const string QueryTemplate = @"
SELECT ?film ?filmLabel ?date ?filmLinks ?actor ?actorLabel ?actorLinks ?birth ?ord WHERE {
  VALUES ?filmType { wd:Q11424 wd:Q24869 }
  ?film wdt:P31 ?filmType ;
        wikibase:sitelinks ?filmLinks ;
        wdt:P577 ?date .
  FILTER(?filmLinks >= {MIN_FILM_LINKS})
  FILTER(YEAR(?date) = {YEAR})
  ?film p:P161 ?castStatement .
  ?castStatement ps:P161 ?actor .
  OPTIONAL { ?castStatement pq:P1545 ?ord . }
  ?actor wikibase:sitelinks ?actorLinks .
  FILTER(?actorLinks >= {MIN_ACTOR_LINKS})
  OPTIONAL { ?actor wdt:P569 ?birth . }
  SERVICE wikibase:label { bd:serviceParam wikibase:language ""en"". }
}";

    public static async Task<DatasetFile> FetchAsync(WikidataOptions o, TextWriter log, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(o.UserAgent);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/sparql-results+json"));

        var rows = new List<SparqlRow>();
        for (int year = o.FromYear; year <= o.ToYear; year++)
        {
            string q = QueryTemplate
                .Replace("{YEAR}", year.ToString())
                .Replace("{MIN_FILM_LINKS}", o.MinFilmSitelinks.ToString())
                .Replace("{MIN_ACTOR_LINKS}", o.MinActorSitelinks.ToString());
            var body = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("query", q) });
            for (int attempt = 1; ; attempt++)
            {
                using var resp = await http.PostAsync(o.Endpoint, body, ct);
                if ((int)resp.StatusCode == 429 && attempt < 5)
                {
                    var wait = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10 * attempt);
                    await log.WriteLineAsync($"  {year}: rate limited, waiting {wait.TotalSeconds:0}s");
                    await Task.Delay(wait, ct);
                    continue;
                }
                resp.EnsureSuccessStatusCode();
                await using var s = await resp.Content.ReadAsStreamAsync(ct);
                var parsed = ParseSparql(await JsonDocument.ParseAsync(s, cancellationToken: ct));
                rows.AddRange(parsed);
                await log.WriteLineAsync($"  {year}: {parsed.Count} cast rows");
                break;
            }
            await Task.Delay(1000, ct); // be polite to the public endpoint
        }
        return Build(rows, $"Wikidata SPARQL {o.FromYear}-{o.ToYear}, film sitelinks >= {o.MinFilmSitelinks}, actor sitelinks >= {o.MinActorSitelinks}");
    }

    internal sealed class SparqlRow
    {
        public string FilmQid = "", FilmLabel = "", ActorQid = "", ActorLabel = "";
        public int? Year, BirthYear, Ord;
        public int FilmLinks, ActorLinks;
    }

    internal static List<SparqlRow> ParseSparql(JsonDocument doc)
    {
        var list = new List<SparqlRow>();
        foreach (var b in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            string? Val(string k) => b.TryGetProperty(k, out var v) ? v.GetProperty("value").GetString() : null;
            list.Add(new SparqlRow
            {
                FilmQid = Qid(Val("film")!),
                FilmLabel = Val("filmLabel") ?? "",
                ActorQid = Qid(Val("actor")!),
                ActorLabel = Val("actorLabel") ?? "",
                Year = YearOf(Val("date")),
                BirthYear = YearOf(Val("birth")),
                Ord = int.TryParse(Val("ord"), out int o) ? o : null,
                FilmLinks = int.TryParse(Val("filmLinks"), out int fl) ? fl : 0,
                ActorLinks = int.TryParse(Val("actorLinks"), out int al) ? al : 0,
            });
        }
        return list;
    }

    internal static DatasetFile Build(IEnumerable<SparqlRow> rows, string notes)
    {
        var file = new DatasetFile { Source = "wikidata", Notes = notes };
        var actors = new Dictionary<long, ActorRow>();
        var movies = new Dictionary<long, MovieRow>();
        var credits = new Dictionary<(long, long), CreditRow>();

        foreach (var r in rows)
        {
            // Unlabelled items come back with their QID as label: skip them, players can't search for them.
            if (IsQidLabel(r.FilmLabel) || IsQidLabel(r.ActorLabel)) continue;
            long fid = NumericId(r.FilmQid), aid = NumericId(r.ActorQid);

            if (!movies.TryGetValue(fid, out var m))
                movies[fid] = m = new MovieRow { Id = fid, WikidataId = r.FilmQid, Title = r.FilmLabel, Year = r.Year, Popularity = r.FilmLinks };
            else if (r.Year is int y && (m.Year is null || y < m.Year)) m.Year = y; // earliest release date

            if (!actors.ContainsKey(aid))
                actors[aid] = new ActorRow { Id = aid, WikidataId = r.ActorQid, Name = r.ActorLabel, BirthYear = r.BirthYear, Popularity = r.ActorLinks };

            var key = (aid, fid);
            if (!credits.TryGetValue(key, out var c))
                credits[key] = new CreditRow { ActorId = aid, MovieId = fid, BillingOrder = r.Ord };
            else if (r.Ord is int o && (c.BillingOrder is null || o < c.BillingOrder)) c.BillingOrder = o;
        }
        file.Actors = actors.Values.OrderBy(a => a.Id).ToList();
        file.Movies = movies.Values.OrderBy(m => m.Id).ToList();
        file.Credits = credits.Values.OrderBy(c => c.MovieId).ThenBy(c => c.ActorId).ToList();
        return file;
    }

    internal static string Qid(string uri) => uri[(uri.LastIndexOf('/') + 1)..];
    internal static long NumericId(string qid) => long.Parse(qid.TrimStart('Q'));
    private static bool IsQidLabel(string s) => s.Length > 1 && s[0] == 'Q' && s.Skip(1).All(char.IsDigit);
    private static int? YearOf(string? isoDate) =>
        isoDate is { Length: >= 4 } && int.TryParse(isoDate.TrimStart('+', '-').AsSpan(0, 4), out int y) ? y : null;
}
