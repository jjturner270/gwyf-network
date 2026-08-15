import { randomUUID } from "node:crypto";
import { describe, expect, it, beforeAll } from "vitest";
import { createClient, migrate, type GwyfDb } from "@gwyf/db";
import {
  deriveObserverId,
  generateObserverKeyPair,
  signBytes,
  type ObserverKeyPair,
} from "@gwyf/protocol";
import { buildApp } from "../apps/api/src/app.js";
import { loadConfig, type ApiConfig } from "../apps/api/src/config.js";

let db: GwyfDb;
let config: ApiConfig;
let api: ReturnType<typeof buildApp>;
let now: () => number;

const NOW = Date.parse("2026-08-15T06:20:00Z");

function advance(ms: number) {
  const base = NOW + ms;
  now = () => base;
}

function iso(offsetMs: number) {
  return new Date(NOW + offsetMs).toISOString();
}

function lobbyBody(keyPair: ObserverKeyPair, overrides: Record<string, unknown> = {}) {
  const observerId = overrides.installationId as string | undefined;
  return JSON.stringify({
    schema: "gwyf.lobby-sighting.v1",
    protocolVersion: 1,
    sightingId: randomUUID(),
    observedAt: iso(0),
    lobby: {
      steamLobbyId: "10977524123456789",
      name: "Fuzzy's Hellhole",
      members: 7,
      capacity: 12,
      map: "Custom Map",
      mode: "Classic",
    },
    observer: {
      installationId: observerId ?? deriveObserverId(keyPair.publicKeyHex),
      clientVersion: "0.1.0",
      searchScope: "default",
    },
    ...overrides,
  });
}

async function ingest(keyPair: ObserverKeyPair, path: string, body: string) {
  return api.inject({
    method: "POST",
    url: path,
    headers: {
      "content-type": "application/json",
      "x-observer-id": deriveObserverId(keyPair.publicKeyHex),
      "x-observer-signature": signBytes(keyPair, body),
    },
    payload: body,
  });
}

beforeAll(async () => {
  const client = createClient(":memory:");
  migrate(client.sqlite);
  db = client.db;
  config = loadConfig({});
  now = () => NOW;
  api = buildApp({
    db,
    config,
    onDeps: (deps) => {
      deps.now = () => now();
    },
  });
  await api.ready();
});

describe("client compatibility config", () => {
  it("serves the compatibility contract used to gate clients", async () => {
    const response = await api.inject({ method: "GET", url: "/v1/client/config" });
    expect(response.statusCode).toBe(200);
    const body = JSON.parse(response.body);
    expect(body.protocolVersion).toBe(1);
    expect(typeof body.configVersion).toBe("number");
    expect(body.minimumClientVersion).toBe("0.3.0");
    expect(body.latestClientVersion).toBe("0.3.0");
    expect(body.supportedGameBuilds).toEqual([303]);
    expect(body.ingestEnabled).toBe(true);
    expect(response.headers["cache-control"]).toContain("no-store");
  });

  it("honors env-driven overrides", async () => {
    const overrideConfig = loadConfig({
      CLIENT_MIN_VERSION: "0.4.0",
      CLIENT_SUPPORTED_GAME_BUILDS: "303,304",
      CLIENT_INGEST_ENABLED: "false",
      CLIENT_CONFIG_VERSION: "7",
    });
    const overrideApp = buildApp({ db, config: overrideConfig });
    await overrideApp.ready();
    const response = await overrideApp.inject({ method: "GET", url: "/v1/client/config" });
    expect(JSON.parse(response.body)).toMatchObject({
      configVersion: 7,
      minimumClientVersion: "0.4.0",
      supportedGameBuilds: [303, 304],
      ingestEnabled: false,
    });
  });
});

describe("observer registration", () => {
  it("registers an observer id derived from its public key", async () => {
    const pair = generateObserverKeyPair();
    const response = await api.inject({
      method: "POST",
      url: "/v1/observers",
      headers: { "content-type": "application/json" },
      payload: JSON.stringify({
        publicKey: pair.publicKeyHex,
        clientVersion: "0.1.0",
        protocolVersion: 1,
      }),
    });
    expect(response.statusCode).toBe(201);
    expect(JSON.parse(response.body).observerId).toBe(deriveObserverId(pair.publicKeyHex));
  });

  it("rejects a malformed public key", async () => {
    const response = await api.inject({
      method: "POST",
      url: "/v1/observers",
      headers: { "content-type": "application/json" },
      payload: JSON.stringify({ publicKey: "nope", clientVersion: "0.1.0", protocolVersion: 1 }),
    });
    expect(response.statusCode).toBe(422);
  });
});

describe("ingest chain", () => {
  const pairA = generateObserverKeyPair();
  const pairB = generateObserverKeyPair();

  it("registers both observers used by the chain", async () => {
    for (const pair of [pairA, pairB]) {
      const response = await ingest(pair, "/v1/observers", JSON.stringify({
        publicKey: pair.publicKeyHex,
        clientVersion: "0.1.0",
        protocolVersion: 1,
      }));
      expect(response.statusCode).toBe(201);
    }
  });

  it("accepts a signed lobby sighting and is idempotent on replay", async () => {
    const body = lobbyBody(pairA);
    const first = await ingest(pairA, "/v1/ingest/lobby-sightings", body);
    expect(first.statusCode).toBe(202);
    expect(JSON.parse(first.body).status).toBe("accepted");

    const replay = await ingest(pairA, "/v1/ingest/lobby-sightings", body);
    expect(replay.statusCode).toBe(202);
    expect(JSON.parse(replay.body).status).toBe("duplicate");

    const count = db.$client
      .prepare("SELECT COUNT(*) AS c FROM lobby_sightings WHERE sighting_id = ?")
      .get(JSON.parse(body).sightingId) as { c: number };
    expect(count.c).toBe(1);
  });

  it("rejects a body mutated after signing", async () => {
    const body = lobbyBody(pairA);
    const signature = signBytes(pairA, body);
    const tampered = body.replace("Fuzzy's Hellhole", "Hijacked Name");
    const response = await api.inject({
      method: "POST",
      url: "/v1/ingest/lobby-sightings",
      headers: {
        "content-type": "application/json",
        "x-observer-id": deriveObserverId(pairA.publicKeyHex),
        "x-observer-signature": signature,
      },
      payload: tampered,
    });
    expect(response.statusCode).toBe(401);
  });

  it("rejects unknown observers", async () => {
    const stranger = generateObserverKeyPair();
    const body = lobbyBody(stranger);
    const response = await ingest(stranger, "/v1/ingest/lobby-sightings", body);
    expect(response.statusCode).toBe(401);
  });

  it("rejects requests without signature headers", async () => {
    const body = lobbyBody(pairA);
    const response = await api.inject({
      method: "POST",
      url: "/v1/ingest/lobby-sightings",
      headers: { "content-type": "application/json" },
      payload: body,
    });
    expect(response.statusCode).toBe(401);
  });

  it("rejects sightings with future timestamps beyond clock skew", async () => {
    const body = lobbyBody(pairA, { observedAt: iso(120_000) });
    const response = await ingest(pairA, "/v1/ingest/lobby-sightings", body);
    expect(response.statusCode).toBe(422);
  });

  it("accepts batch ingest mixing schemas", async () => {
    const body = JSON.stringify({
      schema: "gwyf.batch.v1",
      protocolVersion: 1,
      items: [
        JSON.parse(lobbyBody(pairA)),
        {
          schema: "gwyf.course-sighting.v1",
          protocolVersion: 1,
          sightingId: randomUUID(),
          observedAt: iso(0),
          course: {
            displayName: "THE FUCK HOLE v3",
            sceneName: "CustomLevel",
            fingerprint: "map_sha256_ab",
            objectCount: 1834,
            holeCount: 18,
          },
          observer: { installationId: deriveObserverId(pairA.publicKeyHex), clientVersion: "0.1.0" },
        },
      ],
    });
    const response = await ingest(pairA, "/v1/ingest/batch", body);
    expect(response.statusCode).toBe(202);
    expect(JSON.parse(response.body).accepted).toBe(2);
  });

  it("derives a live view from independent observers without leaking identities", async () => {
    advance(-30_000);
    const fromA = lobbyBody(pairA);
    const fromB = lobbyBody(pairB, { installationId: deriveObserverId(pairB.publicKeyHex) });
    await ingest(pairA, "/v1/ingest/lobby-sightings", fromA);
    await ingest(pairB, "/v1/ingest/lobby-sightings", fromB);

    const response = await api.inject({ method: "GET", url: "/v1/lobbies/live" });
    expect(response.statusCode).toBe(200);
    const payload = JSON.parse(response.body) as {
      count: number;
      lobbies: Array<{ lobbyId: string; name: string; players: number; capacity: number; status: string; confidence: number; recentObservers: number; observerCount: number }>;
    };
    const lobby = payload.lobbies.find((l) => l.lobbyId === "10977524123456789");
    expect(lobby).toBeDefined();
    expect(lobby!.players).toBe(7);
    expect(lobby!.capacity).toBe(12);
    expect(lobby!.recentObservers).toBe(2);
    expect(lobby!.observerCount).toBe(2);
    expect(["active", "likely_active", "stale", "expired"]).toContain(lobby!.status);
    expect(response.body).not.toContain("obs_");
    expect(response.body).not.toContain("publicKey");
  });

  it("marks lobbies stale and then expired as time passes", async () => {
    advance(120_000);
    let response = await api.inject({ method: "GET", url: "/v1/lobbies/live" });
    let lobby = JSON.parse(response.body).lobbies.find((l: { lobbyId: string }) => l.lobbyId === "10977524123456789");
    expect(lobby.status).toBe("stale");

    advance(400_000);
    response = await api.inject({ method: "GET", url: "/v1/lobbies/live" });
    lobby = JSON.parse(response.body).lobbies.find((l: { lobbyId: string }) => l.lobbyId === "10977524123456789");
    expect(lobby).toBeUndefined();

    response = await api.inject({ method: "GET", url: "/v1/lobbies" });
    lobby = JSON.parse(response.body).lobbies.find((l: { lobbyId: string }) => l.lobbyId === "10977524123456789");
    expect(lobby.status).toBe("expired");
    expect(lobby.lastSeenSecondsAgo).toBeGreaterThan(300);
  });

  it("keeps raw rows immutable across re-observation", async () => {
    const rows = db.$client
      .prepare("SELECT lobby_name, member_count FROM lobby_sightings ORDER BY id")
      .all() as Array<{ lobby_name: string; member_count: number }>;
    const before = JSON.stringify(rows);
    advance(0);
    await ingest(pairA, "/v1/ingest/lobby-sightings", lobbyBody(pairA));
    const after = JSON.stringify(db.$client.prepare("SELECT lobby_name, member_count FROM lobby_sightings ORDER BY id").all());
    expect(after).toContain(before.slice(1, -1));
  });

  it("serves lobby detail and history", async () => {
    const detail = await api.inject({ method: "GET", url: "/v1/lobbies/10977524123456789" });
    expect(detail.statusCode).toBe(200);
    expect(JSON.parse(detail.body).lobby.lobbyId).toBe("10977524123456789");

    const history = await api.inject({ method: "GET", url: "/v1/lobbies/10977524123456789/history" });
    expect(history.statusCode).toBe(200);
    expect(Array.isArray(JSON.parse(history.body).sessions)).toBe(true);

    const missing = await api.inject({ method: "GET", url: "/v1/lobbies/00000000000000000" });
    expect(missing.statusCode).toBe(404);
  });
});

// Base clock for the reconciliation/freshness/identity/adversarial slices.
const BASE = NOW + 600_000;

function at(offsetMs: number) {
  return new Date(BASE + offsetMs).toISOString();
}

function lobbySightingBody(
  keyPair: ObserverKeyPair,
  steamLobbyId: string,
  observedAt: string,
  overrides: Record<string, unknown> = {},
) {
  const body = lobbyBody(keyPair, { observedAt, lobby: { steamLobbyId, name: "Fuzzy's Hellhole", members: 7, capacity: 12, map: "Custom Map", mode: "Classic" } });
  const parsed = JSON.parse(body) as Record<string, unknown>;
  parsed.lobby = { steamLobbyId, name: "Fuzzy's Hellhole", members: 7, capacity: 12, map: "Custom Map", mode: "Classic", ...(overrides.lobby as object) };
  return JSON.stringify(parsed);
}

async function register(keyPair: ObserverKeyPair) {
  const response = await ingest(keyPair, "/v1/observers", JSON.stringify({
    publicKey: keyPair.publicKeyHex,
    clientVersion: "0.1.0",
    protocolVersion: 1,
  }));
  expect(response.statusCode).toBe(201);
}

function liveLobby(lobbyId: string, responseBody: string) {
  const payload = JSON.parse(responseBody) as {
    lobbies: Array<{ lobbyId: string; name: string; players: number; status: string; lastSeenAt: string; observerCount: number; recentObservers: number; firstSeenAt: string; sightingCount: number }>;
  };
  return payload.lobbies.filter((l) => l.lobbyId === lobbyId);
}

describe("multi-observer reconciliation", () => {
  const pairC = generateObserverKeyPair();
  const pairD = generateObserverKeyPair();
  const LOBBY = "88001234567890001";

  it("registers the observers used by this slice", async () => {
    await register(pairC);
    await register(pairD);
  });

  it("produces exactly one lobby when two observers see the same id", async () => {
    advance(BASE - NOW);
    const fromC = lobbySightingBody(pairC, LOBBY, at(-30_000));
    const fromD = lobbySightingBody(pairD, LOBBY, at(-20_000));
    await ingest(pairC, "/v1/ingest/lobby-sightings", fromC);
    await ingest(pairD, "/v1/ingest/lobby-sightings", fromD);

    const response = await api.inject({ method: "GET", url: "/v1/lobbies/live" });
    expect(response.statusCode).toBe(200);
    const lobbies = liveLobby(LOBBY, response.body);
    expect(lobbies).toHaveLength(1);
    expect(lobbies[0]!.observerCount).toBe(2);
    expect(lobbies[0]!.recentObservers).toBe(2);
  });

  it("preserves each signed sighting independently", async () => {
    const rows = db.$client
      .prepare(
        "SELECT sighting_id, observer_id, signature, observed_at FROM lobby_sightings WHERE steam_lobby_id = ? ORDER BY id",
      )
      .all(LOBBY) as Array<{ sighting_id: string; observer_id: string; signature: string; observed_at: number }>;
    expect(rows).toHaveLength(2);
    expect(rows[0]!.observer_id).not.toBe(rows[1]!.observer_id);
    expect(rows[0]!.sighting_id).not.toBe(rows[1]!.sighting_id);
    expect(rows[0]!.signature).toBeTruthy();
    expect(rows[1]!.signature).toBeTruthy();
  });

  it("lastSeenAt reflects the freshest valid observation", async () => {
    const lobbies = liveLobby(LOBBY, (await api.inject({ method: "GET", url: "/v1/lobbies/live" })).body);
    expect(lobbies[0]!.lastSeenAt).toBe(at(-20_000));
  });

  it("resolves conflicting metadata deterministically (newest non-null wins)", async () => {
    advance(BASE - NOW + 10_000);
    const renamed = lobbySightingBody(pairD, LOBBY, at(-5_000), {
      lobby: { name: "Renamed", members: 9, capacity: 12 },
    });
    await ingest(pairD, "/v1/ingest/lobby-sightings", renamed);

    const lobbies = liveLobby(LOBBY, (await api.inject({ method: "GET", url: "/v1/lobbies/live" })).body);
    expect(lobbies[0]!.name).toBe("Renamed");
    expect(lobbies[0]!.players).toBe(9);
    expect(lobbies[0]!.sightingCount).toBe(3);
    expect(lobbies[0]!.observerCount).toBe(2);
  });
});

describe("freshness semantics", () => {
  const pairE = generateObserverKeyPair();
  const LOBBY_OFF = "88001234567890002";
  const LOBBY_STAY = "88001234567890003";

  it("registers the observer used by this slice", async () => {
    await register(pairE);
  });

  it("ages a lobby out from observation timestamps alone when the observer goes silent", async () => {
    advance(BASE - NOW);
    await ingest(pairE, "/v1/ingest/lobby-sightings", lobbySightingBody(pairE, LOBBY_OFF, at(-5_000)));

    advance(BASE - NOW + 85_000);
    let lobbies = liveLobby(LOBBY_OFF, (await api.inject({ method: "GET", url: "/v1/lobbies/live" })).body);
    expect(lobbies[0]!.status).toBe("likely_active");

    advance(BASE - NOW + 200_000);
    lobbies = liveLobby(LOBBY_OFF, (await api.inject({ method: "GET", url: "/v1/lobbies/live" })).body);
    expect(lobbies[0]!.status).toBe("stale");

    advance(BASE - NOW + 400_000);
    lobbies = liveLobby(LOBBY_OFF, (await api.inject({ method: "GET", url: "/v1/lobbies/live" })).body);
    expect(lobbies).toHaveLength(0);

    const all = JSON.parse((await api.inject({ method: "GET", url: "/v1/lobbies" })).body);
    const retained = all.lobbies.find((l: { lobbyId: string }) => l.lobbyId === LOBBY_OFF);
    expect(retained.status).toBe("expired");
  });

  it("drops one lobby while others remain visible", async () => {
    advance(BASE - NOW);
    await ingest(pairE, "/v1/ingest/lobby-sightings", lobbySightingBody(pairE, LOBBY_STAY, at(-10_000)));

    advance(BASE - NOW + 300_000);
    await ingest(pairE, "/v1/ingest/lobby-sightings", lobbySightingBody(pairE, LOBBY_STAY, at(295_000)));

    advance(BASE - NOW + 400_000);
    const response = await api.inject({ method: "GET", url: "/v1/lobbies/live" });
    expect(liveLobby(LOBBY_OFF, response.body)).toHaveLength(0);
    const stay = liveLobby(LOBBY_STAY, response.body);
    expect(stay).toHaveLength(1);
    expect(stay[0]!.status).toBe("stale");
  });
});

describe("lobby identity stability", () => {
  const pairF = generateObserverKeyPair();
  const LOBBY_HOST = "77009876543210001";

  it("registers the observer used by this slice", async () => {
    await register(pairF);
  });

  it("host changes never fragment the lobby record", async () => {
    advance(BASE - NOW);
    await ingest(pairF, "/v1/ingest/lobby-sightings", lobbySightingBody(pairF, LOBBY_HOST, at(-120_000), {
      lobby: { name: "Dockside", members: 3, capacity: 12, map: "Forest", mode: "Classic" },
    }));
    await ingest(pairF, "/v1/ingest/lobby-sightings", lobbySightingBody(pairF, LOBBY_HOST, at(-60_000), {
      lobby: { name: "Dockside Arena", members: 5, capacity: 12, map: "Desert", mode: "Classic" },
    }));
    await ingest(pairF, "/v1/ingest/lobby-sightings", lobbySightingBody(pairF, LOBBY_HOST, at(-5_000), {
      lobby: { name: "Dockside Arena", members: 7, capacity: 12, map: "Castle", mode: "Champions" },
    }));

    const lobbies = liveLobby(LOBBY_HOST, (await api.inject({ method: "GET", url: "/v1/lobbies/live" })).body);
    expect(lobbies).toHaveLength(1);
    const lobby = lobbies[0]!;
    expect(lobby.name).toBe("Dockside Arena");
    expect(lobby.status).toBe("active");
    expect(lobby.firstSeenAt).toBe(at(-120_000));
    expect(lobby.lastSeenAt).toBe(at(-5_000));
    expect(lobby.sightingCount).toBe(3);
    expect(lobby.observerCount).toBe(1);
    expect(lobby.players).toBe(7);
  });

  it("lobby persists across repeated browser refreshes", async () => {
    advance(BASE - NOW);
    for (let i = 0; i < 5; i++) {
      await ingest(pairF, "/v1/ingest/lobby-sightings", lobbySightingBody(pairF, LOBBY_HOST, at(-100_000 + i * 5_000)));
    }
    const lobbies = liveLobby(LOBBY_HOST, (await api.inject({ method: "GET", url: "/v1/lobbies/live" })).body);
    expect(lobbies).toHaveLength(1);
    expect(lobbies[0]!.sightingCount).toBe(8);
    expect(lobbies[0]!.firstSeenAt).toBe(at(-120_000));
    expect(lobbies[0]!.lastSeenAt).toBe(at(-5_000));
  });
});

describe("adversarial ingest", () => {
  const pairX = generateObserverKeyPair();
  const pairY = generateObserverKeyPair();
  const LOBBY_SPAM = "6600998877665544";

  it("registers the observers used by this slice", async () => {
    await register(pairX);
    await register(pairY);
  });

  it("rejects a signature made by a different key than the claimed observer", async () => {
    const body = lobbySightingBody(pairX, "12345", at(0));
    const signature = signBytes(pairX, body);
    const response = await api.inject({
      method: "POST",
      url: "/v1/ingest/lobby-sightings",
      headers: {
        "content-type": "application/json",
        "x-observer-id": deriveObserverId(pairY.publicKeyHex),
        "x-observer-signature": signature,
      },
      payload: body,
    });
    expect(response.statusCode).toBe(401);
    expect(JSON.parse(response.body).reason).toBe("bad_signature");
  });

  it("rejects an ancient observation timestamp", async () => {
    const body = lobbySightingBody(pairX, "12345", new Date(NOW - 8 * 24 * 3_600_000).toISOString());
    const response = await ingest(pairX, "/v1/ingest/lobby-sightings", body);
    expect(response.statusCode).toBe(422);
    expect(JSON.parse(response.body).reason).toBe("observedAt older than max ingest age");
  });

  it("rejects a malformed lobby id", async () => {
    const body = lobbySightingBody(pairX, "bad id with spaces!", at(0));
    const response = await ingest(pairX, "/v1/ingest/lobby-sightings", body);
    expect(response.statusCode).toBe(422);
  });

  it("spamming old observations cannot keep a dead lobby alive", async () => {
    advance(BASE - NOW);
    await ingest(pairX, "/v1/ingest/lobby-sightings", lobbySightingBody(pairX, LOBBY_SPAM, at(-5_000)));

    advance(BASE - NOW + 400_000);
    let lobbies = liveLobby(LOBBY_SPAM, (await api.inject({ method: "GET", url: "/v1/lobbies/live" })).body);
    expect(lobbies).toHaveLength(0);

    for (let i = 0; i < 3; i++) {
      const response = await ingest(pairX, "/v1/ingest/lobby-sightings", lobbySightingBody(pairX, LOBBY_SPAM, at(-5_000)));
      expect(response.statusCode).toBe(202);
    }

    lobbies = liveLobby(LOBBY_SPAM, (await api.inject({ method: "GET", url: "/v1/lobbies/live" })).body);
    expect(lobbies).toHaveLength(0);

    const all = JSON.parse((await api.inject({ method: "GET", url: "/v1/lobbies" })).body);
    const retained = all.lobbies.find((l: { lobbyId: string }) => l.lobbyId === LOBBY_SPAM);
    expect(retained.status).toBe("expired");
    expect(retained.lastSeenAt).toBe(at(-5_000));
  });
});