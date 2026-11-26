"""Builds seed-dataset.json (source-neutral DatasetFile) from seed-films.txt.
Run: python3 -I tools/fixtures/build_seed.py tools/fixtures"""
import json, sys, pathlib

root = pathlib.Path(sys.argv[1])
births = {}
for line in (root / "birth-years.txt").read_text(encoding="utf-8").splitlines():
    if line.strip():
        name, year = [p.strip() for p in line.split("|")]
        births[name] = int(year)

actors, movies, credits = {}, [], []
for line in (root / "seed-films.txt").read_text(encoding="utf-8").splitlines():
    if not line.strip() or line.startswith("#"):
        continue
    title, year, cast = [p.strip() for p in line.split("|")]
    movie_id = 910_000_000 + len(movies) + 1
    movies.append({"id": movie_id, "title": title, "year": int(year), "popularity": 0})
    for order, name in enumerate([c.strip() for c in cast.split(";") if c.strip()], start=1):
        if name not in actors:
            actors[name] = {"id": 900_000_000 + len(actors) + 1, "name": name,
                            "birthYear": births.get(name), "popularity": 0}
        credits.append({"actorId": actors[name]["id"], "movieId": movie_id, "billingOrder": order})

# Popularity for the fixture = number of films in the fixture (real data uses sitelinks / page views).
counts = {}
for c in credits:
    counts[c["actorId"]] = counts.get(c["actorId"], 0) + 1
for a in actors.values():
    a["popularity"] = counts[a["id"]]
for m in movies:
    m["popularity"] = sum(1 for c in credits if c["movieId"] == m["id"])

out = {"source": "fixture", "notes": "Hand-typed dev seed (tools/fixtures/seed-films.txt)",
       "actors": list(actors.values()), "movies": movies, "credits": credits}
(root / "seed-dataset.json").write_text(json.dumps(out, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"{len(actors)} actors, {len(movies)} movies, {len(credits)} credits")
