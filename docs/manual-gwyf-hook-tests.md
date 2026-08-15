# Manual GWYF Hook Tests

In-game verification checklist for the real-client lobby hook slice. Some behavior
cannot be honestly unit-tested outside the game — this file is the honest place for it.

## Prerequisites

- BepInEx 6 Bleeding Edge installed for the game's runtime (macOS: IL2CPP build;
  follow `docs/gwyf-hook-inventory.md`).
- `GWYF_REFS` points at the game's interop references
  (`BepInEx/interop/Il2CppAssembly-CSharp.dll`, BepInEx core, UnityEngine).
- `dotnet build` of `mod/GWYFCommunityNetwork.csproj` succeeds and the DLL is in
  `BepInEx/plugins/`.
- Local API + fake observers running per `README.md` (or `ApiBaseUrl` pointed at
  it). Default remains passive.

## Automated baseline (run before in-game work)

```sh
# game-free mod core + signature/canonical/queue/observer tests
dotnet build mod/GWYFCommunityNetwork.csproj
dotnet test mod/test/GWYFCommunityNetwork.Tests.csproj   # 21 tests

# regenerate the TS-side golden fixtures (committed outputs must not drift)
npx tsx scripts/src/fixtures/generate.ts

# protocol + api suite
pnpm test
```

Worth knowing: with no `GWYF_REFS`, `GameRuntime/` (BepInEx plugin, Harmony
patches, the real game bindings) is excluded from the build — the pipeline and
identity layers still compile and pass tests. `GWYF_REFS` must point at the
game's interop refs before building for the game.

The `SessionBrowserBindings` adapter compiles only under `GWYF_REFS`; its member
names are verified against the build-256 metadata (see
`docs/gwyf-hook-inventory.md`) but the runtime read of every field is guarded,
and a capture failure degrades the single invocation instead of affecting the
game.

## Record

Each run must record: game build, mod version, hook adapter version, date, result.

| Game build | Mod | Hook adapter | Date | Result | Notes |
| --- | --- | --- | --- | --- | --- |

## Checklist

### Loading

- [ ] BepInEx loads the plugin (no startup exception)
- [ ] Main menu loads and is fully interactive
- [ ] Multiplayer menu opens normally

### Observation

- [ ] Opening the vanilla public lobby browser triggers `LobbySearchStarted` log
- [ ] Search results produce one `[Observe]` log entry per lobby
- [ ] Real results are logged:
      `[Observe] lobby=109775... players=7/12 map=Forest mode=Classic`
- [ ] Refresh works repeatedly (repeat searches keep producing sightings)
- [ ] Closing/leaving the browser does not crash and does not alter vanilla behavior
- [ ] Search with zero results produces no sightings, no errors

### Vanilla behavior integrity

- [ ] Joining a vanilla lobby still works (results not reordered, not filtered, not
      mutated)
- [ ] Leaving a lobby still works
- [ ] Password-protected lobbies are visible in logs but never transmit the password
- [ ] Mod disabled → vanilla behavior byte-for-byte identical (no side effects)

### Network resilience

- [ ] API offline → game unaffected; sightings queue locally
- [ ] API restart → queued, fresh-enough sightings replay; old ones discarded per
      queue policy (default 15 min)
- [ ] Malformed server response → game unaffected, error recorded in diagnostics
- [ ] Clock skew: server rejects; `CLOCK` row in diagnostics shows out-of-range;
      game unaffected

### Signing / identity

- [ ] Identity persists across game restarts (`observer_identity.json` reappears,
      same `obs_…`)
- [ ] Deleting the identity file resets identity without breaking anything
- [ ] Two clients on one machine / two machines with different identities both
      report the same lobby → `/v1/lobbies/live` shows `recentObservers = 2`

### Hook failure modes

- [ ] Simulate binding break (rename member in `ReflectionBindings` temporarily):
      logs `HOOK UNAVAILABLE`, no telemetry, game continues
- [ ] Two rapid searches while upload is running: new events enqueue, never block
- [ ] Queue file corruption: quarantined, fresh queue, identity preserved
- [ ] Game log contains no exceptions originating from the mod after any of the above

## Acceptance (milestone)

```text
Client A + Client B both observing lobby X
curl http://localhost:3000/v1/lobbies/live
  → X present with recentObservers: 2, confidence increased
Client B stops refreshing
  → recentObservers falls, freshness decays
Lobby disappears
  → /live drops it after expiry; /v1/lobbies retains history
```