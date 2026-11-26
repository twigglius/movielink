# Tier 1: Unity Editor checklist

Play mode, `LocalPlatform` + `FakeGameApi` (`useFakeApi: true`). No server needed.

- [ ] Main screen shows "Movie Link", tagline, Settings, Account (initials), New Game, How to play, Credits.
- [ ] Settings opens; toggles save across Play sessions; "Show posters" is disabled; Close and tap-outside dismiss.
- [ ] Account shows name, matches played, best score, placeholder note.
- [ ] New Game starts a match: start and target cards show names and initials; timer counts down from 2:00.
- [ ] Search hint says "movie" after an actor and "co-star" after a movie; results appear after typing 2+ letters.
- [ ] Picking a movie the actor was not in shows "That actor isn't credited in that movie."
- [ ] Picking something already in the chain shows "That's already in your chain."
- [ ] Back removes one step; the timer keeps running.
- [ ] Reaching the target auto-submits and shows Results with score, time, your chain and the shortest chain.
- [ ] Score matches the formula table in `shared/MovieLink.Core.Tests/Fixtures/score-cases.json`.
- [ ] Letting the timer hit 0 submits score 0 ("Out of time").
- [ ] Give up submits score 0 and shows Results.
- [ ] Continue returns to the main screen; Account's matches played went up by one.
- [ ] Window resized to landscape: layout stays a centred portrait column.
