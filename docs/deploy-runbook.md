# Deploy runbook — Render (public API + radar)

Two services on Render, reproduced from `render.yaml` (a Blueprint):

| Service | Kind | Origin |
| --- | --- | --- |
| `gwyf-api` | Web (Docker, Node + SQLite) | `https://api.gwyf-community.dev` (PLACEHOLDER) |
| `gwyf-radar` | Static site (publishes `apps/web`) | `https://lobbies.gwyf-community.dev` (PLACEHOLDER) |

These `.dev` names are **placeholders**, not production truth. Until real DNS
points at the Render services, the live origin is whatever Render assigns
(e.g. `https://gwyf-api.onrender.com`). The installer is baked with the **actual
verified service URL**, never the placeholder — `build-setup.ps1` now refuses it
(`-AllowPlaceholder` is for nothing; see `scripts/build-setup.ps1`).

## Rules that are not negotiable

1. **Exactly one `gwyf-api` instance.** SQLite on a persistent disk is a
   single-writer architecture. No replicas, no autoscaling. Containing this is
   intentional and not a knob to turn.
2. **The DB lives on the persistent disk only.** `DATABASE_PATH=/data/gwyf.db`,
   mounted from the `gwyf-data` disk. Ephemeral instances get the same data on
   restart/rebuild precisely because it is on the disk.
3. **Never bake a placeholder or unverified origin into the installer.**

## Step 1 — create services (Blueprint)

1. Render dashboard → **New +** → **Blueprint** → connect this repo.
2. Render reads `render.yaml` and proposes both services. Set:
   - `gwyf-api`: **paid plan** that includes a persistent disk (a `starter`
     service attaches the 1 GB `gwyf-data` disk at `/data`). Confirm the disk
     is listed before deploying.
   - `gwyf-radar`: static site, `buildCommand: node scripts/render-bake-config.mjs`,
     `publishPath: apps/web`, env `API_BASE_URL=https://api.gwyf-community.dev`.
3. Deploy. Wait for `gwyf-api` to be `Live` and `gwyf-radar` published.

## Step 2 — custom domains

Only after the services are live:

- `api.gwyf-community.dev` → CNAME to the `gwyf-api.onrender.com` URL,
  enable auto-TLS.
- `lobbies.gwyf-community.dev` → CNAME to the `gwyf-radar.onrender.com` URL,
  enable auto-TLS.

## Step 3 — verify (save this evidence)

```sh
# 1. healthy
curl -sS https://api.gwyf-community.dev/healthz

# 2. compatibility contract — save verbatim to the release run evidence
curl -sS https://api.gwyf-community.dev/v1/client/config > production-client-config.json
cat production-client-config.json
# expect exactly: protocolVersion, configVersion, minimumClientVersion,
#               latestClientVersion, supportedGameBuilds, ingestEnabled

# 3. public read surface is cross-origin readable
curl -sS -D - -o /dev/null https://api.gwyf-community.dev/v1/lobbies/live | grep -i ^access-control-allow-origin

# 4. the radar page points at the real API (its baked config)
curl -sS https://lobbies.gwyf-community.dev/ | grep GWYF_API_BASE_URL

# 5. cold boot: trigger a redeploy/restart of gwyf-api, then immediately:
#    the FIRST client fetch of config succeeds and a signed ingest is accepted
#    (locally this exact scenario is `pnpm build:smoke` — boots the compiled
#    server under plain Node on a fresh database, then the same database again)
```

## Step 4 — bake the real origin into the installer

Once the API has a real, verified URL:

```pwsh
$env:GWYF_REFS = "path/to/BepInEx/interop"
pwsh scripts/build-setup.ps1 -IngestUrl https://<actual-verified-url> -Version 0.3.0
#     -> dist/GWYF-Community-Setup-0.3.0.exe  (+ SHA-256, selftest PASS)
```

`build-setup.ps1` fails closed on loopback endpoints unless `-AllowLoopback`,
and now also refuses the placeholder `*.gwyf-community.dev` hosts. The baked URL
is whatever the installer actually talks to — verify with the game machine
acceptance in `docs/manual-gwyf-hook-tests.md`.

## Operations

- **Redeploys/rebuilds are safe**: the disk keeps `gwyf.db` across deploys.
- **If `gwyf-api` ever loses its disk** (service deletion), history is gone —
  treat `gwyf-data` as precious, and back it up out of band when the network is
  real.
- **Logs**: `Render → gwyf-api → Logs`; startup prints applied migrations and
  the listening address.
- **Ingest is always signed; GET is public.** Rate limit and clock-skew guards
  are active in production via the same code path `tests/api.test.ts` pins.