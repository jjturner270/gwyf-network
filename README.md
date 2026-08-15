# GWYF Community Network

A crowdsourced observability network and historical atlas for Golf With Your Friends.

Or, less respectable: FlightRadar24 for stupid little golf balls.

Every participating GWYF client observes a slice of the game's public lobby ecosystem and reports it to a shared service. No single client can see the whole picture alone — many boring observations become something useful.

**Observe the game, not the players.**

Lobbies, maps, settings, player counts, activity, timestamps. Not player histories, not relationships, not locations.

## Status

Phase 0–2 of the MVP is built and proven end-to-end:

- Protocol schemas (`lobby-sighting.v1`, `course-sighting.v1`, `runtime-event.v1`, `batch.v1`)
- Signed ingest — append-only, idempotent, rate-limited
- Lobby derivation with explainable confidence (`active` / `likely_active` / `stale` / `expired`)
- Live radar API with filters and session inference
- A fake-mod harness that simulates independent observers without launching GWYF

Not yet built (deliberately): map atlas, course fingerprints, accounts, active scanning, community tags, frontend beyond raw JSON.

## Architecture

```
GWYF client (mod)        → sensor
POST /v1/ingest/*        → collector (signed, validated)
SQLite (append-only)     → memory
derivation services      → interpretation (on-read, pure over sightings)
GET /v1/lobbies/*        → interface
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
scripts/        emit-lobby.ts — fake-mod dev harness
mod/            BepInEx C# skeleton (Plugin, observers, queue, network, diagnostics)
tests/          vitest: protocol, derivation, and the full API chain
```

## Quickstart

Requires Node >= 20 and pnpm.

```bash
pnpm install

pnpm dev                # API on :3000 (first boot applies migrations)
pnpm dev:emit-lobby     # in another terminal — two fake mod clients
curl -s localhost:3000/v1/lobbies/live
```

The emit script simulates three lobbies: one corroborated by both observers, one that fades (watch it degrade `active → likely_active → stale → expire`), one single-observer. Observer keys persist in `scripts/.keys/` so restarts keep the same identity.

```bash
pnpm test               # 35 tests: protocol, derivation, API chain
pnpm build              # typecheck + compile all packages
```

## API

| Method | Path | Notes |
| --- | --- | --- |
| POST | `/v1/observers` | register an ed25519 public key → observer id |
| POST | `/v1/ingest/lobby-sightings` | signed, `202` (or `duplicate`), `422` on clock skew |
| POST | `/v1/ingest/course-sightings` | same contract |
| POST | `/v1/ingest/events` | diagnostic runtime events |
| POST | `/v1/ingest/batch` | mixed schemas, per-item results |
| GET | `/v1/lobbies/live` | radar: last 5 min, `?status=&map=&mode=&sort=&minPlayers=` |
| GET | `/v1/lobbies` | full listing, `?sinceHours=` |
| GET | `/v1/lobbies/:steamLobbyId` | current derived state |
| GET | `/v1/lobbies/:steamLobbyId/history` | inferred sessions, 30-min gap splits |

Ingest requires `X-Observer-Id` and `X-Observer-Signature` (ed25519 over the raw request body). Anything a previous sighting id also carries is `duplicate` — replaying is safe forever.

## Confidence heuristic

Placeholders until real traffic exists (all constants in `apps/api/src/services/derive-lobby-state.ts`):

```
active         newest sighting ≤ 15s OR ≥2 observers within 45s
likely_active  newest sighting ≤ 90s
stale          newest sighting ≤ 5 min
expired        everything older
```

Confidence blends age against 5 minutes, corroboration (capped at 2 observers), recent sighting rate, and penalizes population instability.

## The mod (`mod/`)

BepInEx skeleton with the game sealed behind two hook classes:

- `UnityLobbyHook` — wire the lobby browser results event here
- `UnityCourseHook` — wire scene load events here

Point `GWYF_REFS` at your folder of BepInEx/UnityEngine/Assembly-CSharp DLLs and `dotnet build`. The identity, queue, observers, and network client compile without the game and mirror the wire protocol 1:1. `ObserverIdentity.Sign` is stubbed (`NotImplementedException`) pending an ed25519 library that fits the game's netstandard2.0/Unity runtime.

## Privacy boundary

Public by default: lobby ids, names, player counts, maps, timestamps, aggregate activity, fingerprints, confidence. Never public: durable Steam ids, join history, relationships, IPs, hardware fingerprints, behavioral profiles. Observer identities are per-installation keypairs — a human never attaches identity unless they choose to.

## Next slices

1. Radar web page — the public surface becomes more informative than one player's vanilla browser
2. Course fingerprints (`SceneFingerprint` needs a real normalized scene snapshot pipeline)
3. Map atlas + version lineage
4. Community layer (tags, creator claims) — only after the observation network produces useful data on its own