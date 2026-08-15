# GWYF Community Network

A crowdsourced observability network and historical atlas for Golf With Your Friends.

Or, less respectable: FlightRadar24 for stupid little golf balls.

Every participating GWYF client observes a slice of the game's public lobby ecosystem and reports it to a shared service. No single client can see the whole picture alone — many boring observations become something useful.

**Observe the game, not the players.**

Lobbies, maps, settings, player counts, activity, timestamps. Not player histories, not relationships, not locations.

## Status

Phases 0–2 of the MVP plus the multi-observer truth slice are built and proven:

- Protocol schemas (`lobby-sighting.v1`, `course-sighting.v1`, `runtime-event.v1`, `batch.v1`)
- Signed ingest — append-only, idempotent, rate-limited, clock-bounded
- Multi-observer reconciliation — one lobby per `steamLobbyId` no matter how many
  observers see it, per-observer sightings preserved, deterministic metadata
  resolution, `observerCount` tracked
- Formalized freshness — `live`/`likely_active`/`stale`/`expired` derived purely
  from observation timestamps; observers never send a "goodbye"
- Lobby identity stability — host name/map/mode/player changes and repeated
  browser refreshes all resolve to the same lobby record
- Adversarial ingest coverage — tampered payloads, wrong keys, duplicates,
  clock skew (future + ancient), malformed ids, and old-observation spam all
  rejected or harmless
- Live radar API with filters and session inference
- A fake-mod harness that simulates independent observers without launching GWYF
- A real game hook (`UnityLobbyHook` + Harmony bindings) that turns a vanilla GWYF
  lobby-browser search into signed, queued, batched observations — cross-language
  signing proven with shared golden fixtures (C# signs, TypeScript verifies)
- Reproducible release packaging (`scripts/package-mod.ps1` → BepInEx layout ZIP)

Not yet built (deliberately): map atlas, course fingerprints, accounts, active scanning, community tags, frontend beyond raw JSON.

## Architecture

```
GWYF client (mod)        → sensor
GET  /v1/client/config   → compatibility gate (fail-closed before observing)
POST /v1/ingest/*        → collector (signed, validated)
SQLite (append-only)     → memory
derivation services      → interpretation (on-read, pure over sightings)
GET /v1/lobbies/*        → interface
apps/web (static page)   → public lobby radar, zero-auth, polls /v1/lobbies/live
```

Core invariants:

1. **A sighting is not truth** — every observation is `client X observed Y at time Z`. Derived state is computed from sightings, never stored as canonical fact.
2. **Raw observations are append-only** — history is never rewritten to make the present cleaner.
3. **Confidence is explicit** — statuses and confidence scores ship with the inputs that produced them (`lastSeenSecondsAgo`, `recentObservers`, `recentSightings`, `populationCv`), not just a label.
4. **The network is optional** — if the service disappears, GWYF works exactly as before.

## Layout

```
packages/
  protocol/     zod schemas + ed25519 signing + observer id derivation
  db/           drizzle schema, SQL migrations, WAL better-sqlite3 client
apps/
  api/          Fastify: ingest, observers, public lobby routes, derivation
  web/          static public lobby radar (no build, one runtime config: API base URL)
scripts/        emit-lobby.ts — fake-mod dev harness
mod/            BepInEx C# skeleton (Plugin, observers, queue, network, diagnostics)
tests/          vitest: protocol, derivation, the web page, and the full API chain
```

## Quickstart

Requires Node >= 20 and pnpm.

```bash
pnpm install

pnpm dev                # API on :3000 (first boot applies migrations)
pnpm dev:emit-lobby     # in another terminal — two fake mod clients
curl -s localhost:3000/v1/lobbies/live

# public lobby radar — open apps/web/index.html in a browser (no server needed)
pnpm --filter @gwyf/web build  # validates the page runs as a module; serves nothing
```

The radar page has no build step and no dependencies. `apps/web/src/config.js`
sets the default API base URL (`http://127.0.0.1:3000`), or override per page
visit with a query param: `index.html?api=http://127.0.0.1:3000`.

The emit script simulates three lobbies: one corroborated by both observers, one that fades (watch it degrade `active → likely_active → stale → expire`), one single-observer. Observer keys persist in `scripts/.keys/` so restarts keep the same identity.

```bash
pnpm test               # 83 tests: protocol, derivation, reconciliation, API chain, web page
pnpm build              # typecheck + compile all packages
```

## API

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/v1/client/config` | compatibility contract the client gates against (min client version, supported game builds, ingest enabled) |
| POST | `/v1/observers` | register an ed25519 public key → observer id |
| POST | `/v1/ingest/lobby-sightings` | signed, `202` (or `duplicate`), `422` on clock skew |
| POST | `/v1/ingest/course-sightings` | same contract |
| POST | `/v1/ingest/events` | diagnostic runtime events |
| POST | `/v1/ingest/batch` | mixed schemas, per-item results |
| GET | `/v1/lobbies/live` | radar: last 5 min, `?status=&map=&mode=&sort=&minPlayers=`; anonymous GETs get `Access-Control-Allow-Origin: *` for the public page; includes `activeObservers` (active observers who reported in the last 45 s) |
| GET | `/v1/lobbies` | full listing, `?sinceHours=` |
| GET | `/v1/lobbies/:steamLobbyId` | current derived state |
| GET | `/v1/lobbies/:steamLobbyId/history` | inferred sessions, 30-min gap splits |

Lobby responses carry both `recentObservers` (distinct observers in the last 45 s)
and `observerCount` (distinct observers within the observation window — the retained
sightings of that lobby; not "everyone who has ever seen it").

Ingest requires `X-Observer-Id` and `X-Observer-Signature` (ed25519 over the raw request body). Anything a previous sighting id also carries is `duplicate` — replaying is safe forever. `observedAt` must be within the clock-skew window (default 60 s into the future) and no older than the max ingest age (default 7 days). The 7-day bound is a transport/integrity bound: an accepted old sighting is historical evidence, never presence — freshness derives only from `observedAt`.

## Multi-observer reconciliation

Multiple observers seeing the same lobby is the point of the network, so the
rules are explicit and deterministic (implemented in
`apps/api/src/services/derive-lobby-state.ts`):

1. **Identity is the `steamLobbyId`** — every sighting of the same lobby id is
   one lobby, however many observers report it or however the lobby evolves.
   There is no separate lobby table; the lobby is the grouping over append-only
   sightings.
2. **Each signed sighting is preserved independently** — raw rows are never
   merged or rewritten; `sightingCount` and `observerCount` count them.
3. **`players` / `capacity`** come from the newest sighting.
4. **`name` / `map` / `mode`** come from the newest sighting that carries a
   non-null value — a null field never erases a known value (host renames are
   reflected; a partial observation doesn't blank the lobby; once a value is
   known, an observer that can no longer determine it can't clear it —
   intentional).
5. **Ties on `observedAt`** break on the smallest observer id in UTF-16
   code-unit order (the JS default string comparison), so the same input
   always derives the same output.
6. **`lastSeenAt`** is the freshest valid observation; **`firstSeenAt`** the
   oldest. Observation timestamps are the only clock that matters.

## Confidence heuristic

Placeholders until real traffic exists (all constants in `apps/api/src/services/derive-lobby-state.ts`):

```
active         newest sighting ≤ 15s OR ≥2 observers within 45s
likely_active  newest sighting ≤ 90s
stale          newest sighting ≤ 5 min
expired        everything older
```

## Freshness semantics

There are no join/leave or "goodbye" messages in the protocol — a lobby goes
live, ages, and expires purely from the `observedAt` timestamps of valid
observations. An observer that stops refreshing just fades out; resubmitting
old observations with new sighting ids cannot keep a dead lobby alive.

```
active          newest observation ≤ 15 s OR ≥2 observers within 45 s
likely_active   newest observation ≤ 90 s (single observer)
stale           newest observation ≤ 5 min — still listed on /live
expired         everything older — dropped from /live, history retained on /v1/lobbies
```

Confidence blends age against 5 minutes, corroboration (capped at 2 observers), recent sighting rate, and penalizes population instability.

## The mod (`mod/`)

BepInEx plugin with the game sealed behind two hook classes:

- `UnityLobbyHook` — observes the vanilla lobby browser via Harmony patches on
  `SessionBrowserUIController.UpdateSessionInfos` / `EnumerateSessionInfosGameState.RequestSessionInfos`
  (adapter: `SessionBrowserBindings`, seams documented in `docs/gwyf-hook-inventory.md`)
- `UnityCourseHook` — scene load events (course observation is still opt-in, see below)

Passive by default: hooks never trigger searches, never mutate results, and never
block the game. Observations flow through `LobbyObserver` → `ObservationQueue`
(persisted JSONL, crash-tolerant, bounded, replay-on-startup) → signed batch
`POST /v1/ingest/batch`. `ObserverIdentity` is a persistent per-installation
Ed25519 keypair (BouncyCastle, works on the game's runtime); observer id
derivation matches the TypeScript server byte-for-byte, proven by the shared
golden fixtures in `scripts/fixtures/` (regenerate with
`npx tsx scripts/src/fixtures/generate.ts`).

Point `GWYF_REFS` at your folder of BepInEx/UnityEngine/Assembly-CSharp DLLs to
compile the game-bound layer and `dotnet build`. Everything else (identity,
queue, observers, network client) builds and tests without the game.
Without `GWYF_REFS` the plugin layer is excluded and the pipeline runs in
instrument-only mode (`HOOK UNAVAILABLE`). Course observation is disabled until
course fingerprints are validated live; deleting the identity file under
`BepInEx/gwyf-community/` resets the observer identity.

### Configuration

`BepInEx/config/com.gwyf.community.cfg` (created on first run) is the source of
truth — `network.apiBaseUrl`, `network.enabled`, `observation.lobbyEnabled`,
`queue.*`, `diagnostics.verboseLogging`. Environment variables remain as
overrides for dev/test runs.

### Support surface

State lives under `BepInEx/gwyf-community/`:
`observer_identity.json` (keypair), `observer-id.txt` (the public observer id —
include it when reporting issues), `diagnostics.txt` (rendered version/build/
hook/network diagnostics, refreshed on each flush), `observation_queue.jsonl`.

### Release packaging

```pwsh
$env:GWYF_REFS = "path/to/BepInEx/interop"
pwsh scripts/package-mod.ps1        # -> dist/GWYFCommunityNetwork-<version>.zip
```

Builds `Release` with deterministic settings, stages the BepInEx
`plugins/GWYFCommunityNetwork/` layout (mod DLL + BouncyCastle runtime dep +
README), and zips it. The DLL is reproducible (same refs + same sources → same
bytes); the ZIP container itself is not normalized (timestamps/order), so
successive ZIP hashes may differ even though the build and layout are stable.

### One-click installer (`installer/`)

`GWYF-Community-Setup-<version>.exe` is the distribution artifact a stranger
double-clicks — no unzipping, no terminal, no training:

```pwsh
$env:GWYF_REFS = "path/to/BepInEx/interop"
pwsh scripts/build-setup.ps1 -IngestUrl https://api.gwyf-community.app
#     -> dist/GWYF-Community-Setup-<version>.exe
```

A release build **must** bake a real ingest endpoint: the pipeline fails a
build whose baked endpoint is loopback unless `-AllowLoopback` is passed (dev
builds only), so a `127.0.0.1` build can never ship by accident. The baked
endpoint is compiled into the exe and pre-seeds the mod config, so a normal
user never opens a config file. Dev/test iterations use
`-IngestUrl http://127.0.0.1:3000 -AllowLoopback`.

The setup exe is a self-contained net48 WinForms app (runs on any Windows
10/11, nothing to install). It embeds the mod release zip, finds the Golf With
Your Friends install via Steam registry + `libraryfolders.vdf` (manual
"choose game folder" only as a fallback), detects or installs BepInEx 6
IL2CPP be.785 (pinned, verified against game build 303, SHA-256-verified
before it is ever extracted), deploys the plugin, seeds the config file, and
shows Install / Repair / Launch Game / Uninstall.
BepInEx 5 or unknown-version installs are upgraded by overlaying be.785.
Uninstall removes the plugin/config/identity and offers to remove BepInEx only
when this setup exe installed it and no other plugins remain.

Non-destructive verification of the artifact:

```text
GWYF-Community-Setup.exe --selftest              headless discovery + package probe
GWYF-Community-Setup.exe --selftest-install DIR  full install/uninstall cycle in a sandbox
```

The installer owns mutation of the game installation; the mod only observes the
game. Upgrades are a new installer run (`Repair`), never self-modification
inside the game process.

## Privacy boundary

Public by default: lobby ids, names, player counts, maps, timestamps, aggregate activity, fingerprints, confidence. Never public: durable Steam ids, join history, relationships, IPs, hardware fingerprints, behavioral profiles. Observer identities are per-installation keypairs — a human never attaches identity unless they choose to.

## Next slices

1. Deploy the API and the static `apps/web` radar page (public URL) and bake that
   origin into the installer so a stranger's page shows lobbies without config
2. Course fingerprints (`SceneFingerprint` needs a real normalized scene snapshot pipeline)
3. Map atlas + version lineage
4. Community layer (tags, creator claims) — only after the observation network produces useful data on its own

The current milestone — **Distributed Observation Slice: two clients, one truth** —
needs only the in-game two-machine run: two observers on the same public lobby,
the server retaining both signed observations, `/v1/lobbies/live` exposing one
reconciled lobby with `observerCount: 2`, and the lobby aging out when
observations stop (checklist in `docs/manual-gwyf-hook-tests.md`).