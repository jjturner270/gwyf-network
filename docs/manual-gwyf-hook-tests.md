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
| 303 | 0.2.0 | gwyf-lobby-hook-v1 | 2026-08-15 | PASS | Live end-to-end: observer registers, real lobbies flush as signed batches, `/v1/lobbies/live` returns them. BepInEx BE.785+. Unit-verified items are marked below; in-game items were exercised in this run. |

## Checklist

### Loading

- [x] BepInEx loads the plugin (no startup exception)
- [x] Main menu loads and is fully interactive
- [x] Multiplayer menu opens normally

### Observation

- [x] Opening the vanilla public lobby browser triggers `LobbySearchStarted` log
- [x] Search results produce exactly one `[Hook] search refresh: N sessions` log
      line per refresh (per-lobby detail only under `verboseLogging = true` /
      `GWYF_VERBOSE=1`: `[Hook] session[i]: id=… name=… members=…/… map=… mode=…`)
- [x] Real results are observed and uploaded (live verification hit
      `/v1/lobbies/live` with real lobby ids; per-lobby detail is behind the
      verbose gate to keep the log quiet)
- [x] Refresh works repeatedly (repeat searches keep producing sightings)
- [x] Closing/leaving the browser does not crash and does not alter vanilla behavior
- [x] Search with zero results produces no sightings, no errors

### Vanilla behavior integrity

- [x] Joining a vanilla lobby still works (results not reordered, not filtered, not
      mutated)
- [x] Leaving a lobby still works
- [x] Password-protected lobbies are visible in logs but never transmit the password
      (bindings never read `SessionInfo.Password`; privacy rule in
      `docs/lobby-metadata-inventory.md`)
- [x] Mod disabled → vanilla behavior byte-for-byte identical (no side effects;
      only postfix/prefix observation patches, zero mutation paths)

### Network resilience

- [x] API offline → game unaffected; sightings queue locally
      (unit: `TransportFailure_KeepsItemsAndReportsError`)
- [x] API restart → queued, fresh-enough sightings replay; old ones discarded per
      queue policy (default 15 min) (unit: `ExpiredItems_AreDiscardedWithoutUpload`)
- [x] Malformed server response → game unaffected, error recorded in diagnostics
- [x] Clock skew: server rejects; `CLOCK` row in diagnostics shows out-of-range;
      game unaffected (unit + server test)

### Signing / identity

- [x] Identity persists across game restarts (`observer_identity.json` reappears,
      same `obs_…`) (unit: `Queue_PersistsAndReloads` covers file persistence;
      identity file behavior identical)
- [x] Deleting the identity file resets identity without breaking anything
      (fresh keypair + new observer id on next load)
- [ ] Two clients on one machine / two machines with different identities both
      report the same lobby → `/v1/lobbies/live` shows `recentObservers = 2` and
      `observerCount = 2` — **next milestone acceptance** (server side proven in
      vitest `multi-observer reconciliation`; in-game two-client run pending)

### Hook failure modes

- [x] Simulate binding break (rename member in `ReflectionBindings` temporarily):
      logs `HOOK UNAVAILABLE`, no telemetry, game continues
- [x] Two rapid searches while upload is running: new events enqueue, never block
- [x] Queue file corruption: quarantined, fresh queue, identity preserved
      (unit: `CorruptQueueFile_IsQuarantined`)
- [x] Game log contains no exceptions originating from the mod after any of the above

## Smoke (next launch — quiet logs + flush ACK)

Run once at the next game launch, before any feature work:

- [ ] With default config (no `GWYF_VERBOSE`, `verboseLogging = false`): opening the
      lobby browser logs only `[Hook] lobby search started` and
      `[Hook] search refresh: N sessions` — no `[Hook] session[i]` lines, no
      `key=value` metadata dumps
- [ ] After a search batch uploads, exactly one
      `flushed N sightings (accepted=…, duplicates=…, rejected=…)` line appears
      per ack — nothing per-sighting
- [ ] `BepInEx/gwyf-community/observer-id.txt` contains the same `obs_…` id as the
      `observer identity …` startup line
- [ ] `BepInEx/gwyf-community/diagnostics.txt` exists and shows hook adapter,
      game build, and mod version

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

## Acceptance — two-machine distributed proof (release acceptance)

**Freeze:** no feature work. These runs are acceptance only; touch code only if
one of them exposes an actual defect.

Save every artifact below into a dated folder (e.g. `docs/runs/2026-08-15-two-machine/`)
so the night never has to be reconstructed from logs.

| Evidence item | Value |
| --- | --- |
| Date / server commit |  |
| Observer A id (machine A) | from `BepInEx/gwyf-community/observer-id.txt` |
| Observer B id (machine B) |  |
| Shared `steamLobbyId` |  |
| Lobby observed (name/map/mode) |  |
| A first sighting `observedAt` |  |
| B first sighting `observedAt` |  |
| Last A sighting before closing |  |
| Last B sighting before closing |  |
| Result | PASS / FAIL (reason) |

Snapshot commands (run on the server machine while both clients observe):

```bash
curl -s localhost:3000/v1/lobbies/live > lobby-live-both.json
curl -s localhost:3000/v1/lobbies/<steamLobbyId> > lobby-both.json
curl -s localhost:3000/v1/lobbies/<steamLobbyId>/history > lobby-both-history.json
```

Prove two independent signed rows (not two rows from one client):

```bash
sqlite3 data/gwyf.db "SELECT observer_id, observed_at, signature FROM lobby_sightings WHERE steam_lobby_id='<steamLobbyId>' ORDER BY id;"
```

Decay polling — after A closes, poll ~6 times at 15 s; after B closes, poll at
~1, 2, 5, 8 min to catch each freshness state:

```bash
for i in 1 2 3 4 5 6; do curl -s localhost:3000/v1/lobbies/<steamLobbyId> > decay-$i.json; sleep 15; done
```

Checklist:

- [ ] Both clients on the same public lobby; server log shows two distinct observer ids
- [ ] `/v1/lobbies/live` exposes **one** lobby with `observerCount: 2` and `recentObservers: 2` — `lobby-live-both.json` saved
- [ ] Raw rows show both observers, distinct signatures and `observedAt`s
- [ ] A closes → within the observer window `recentObservers` drops to 1 while the lobby stays live
- [ ] B closes → lobby progresses `active → likely_active → stale → expired`; decay captures saved
- [ ] `/v1/lobbies` retains history: `status: expired`, `lastSeenAt` at B's last sighting
- [ ] Old-sighting replay does not resurrect it (`/v1/lobbies/live` stays empty)
- [ ] No code changes required → tag `v0.2.0`

## Packaged-build smoke (release acceptance)

Test **the ZIP artifact** (`dist/GWYFCommunityNetwork-<version>.zip`), not the
dev tree.

1. `pwsh scripts/package-mod.ps1` → record ZIP path + SHA-256
   (`Get-FileHash dist/GWYFCommunityNetwork-<version>.zip -Algorithm SHA256`)
2. Extract into a clean-ish BepInEx install (back up any existing
   `BepInEx/gwyf-community/` and the config file first)
3. First launch: capture full log tail to file
4. Open the vanilla lobby browser, search, capture the log tail again
5. Close the game, relaunch once, capture the log tail
6. Verify identity and config survive the restart

| Evidence item | Value |
| --- | --- |
| ZIP path / SHA-256 |  |
| Extracted into |  |
| First-launch log | `smoke-first-launch.log` |
| Search/flush log | `smoke-flush.log` |
| Post-restart log | `smoke-restart.log` |
| Observer id first launch |  |
| Observer id after restart |  |
| Result | PASS / FAIL (reason) |

Checklist (covers the earlier quiet-logs smoke plus artifact and persistence):

- [ ] Fresh extract has no `gwyf-community` state → created on first run
- [ ] `BepInEx/config/com.gwyf.community.cfg` created with defaults bound
      (`network.apiBaseUrl = http://127.0.0.1:3000`, observation enabled)
- [ ] `observer-id.txt` matches the `observer identity …` startup line
- [ ] `diagnostics.txt` shows hook adapter, game build, mod version
- [ ] Default logging quiet: only `[Hook] lobby search started` and
      `[Hook] search refresh: N sessions` per refresh
- [ ] Exactly one `flushed N sightings (accepted=…, duplicates=…, rejected=…)`
      per ack — nothing per-sighting
- [ ] Ingest accepted server-side (row exists / `/v1/lobbies/live` returns the lobby)
- [ ] Restart: same observer id, same config values, clean startup
- [ ] No code changes required → tag `v0.2.0`

## Release

```text
git tag -a v0.2.0 -m "Distributed Observation"
git push origin v0.2.0
```

Milestone: independent game clients produce signed lobby observations that
reconcile deterministically into a single freshness-aware network view.