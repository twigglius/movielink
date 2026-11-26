# Tier 2: Dev web build checklist

Dev WebGL build (`MovieLink > Build Dev WebGL`) with `useFakeApi: false`, against a local or staging API.

- [ ] Everything in Tier 1, on desktop Chrome, desktop Safari and a phone browser.
- [ ] Search finds "amelie" → Amélie, "matrix" → The Matrix, "penelope" → Penélope Cruz.
- [ ] Two browsers on the same seed (curl `POST /api/matches` with the same `puzzleSeed`) get the same puzzle.
- [ ] Airplane mode mid-match: step shows "Connection lost. Try again."; Submit retries and never double-counts.
- [ ] Load time and memory OK on an iPhone in Safari.
