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
      lobbies: Array<{ lobbyId: string; name: string; players: number; capacity: number; status: string; confidence: number; recentObservers: number }>;
    };
    const lobby = payload.lobbies.find((l) => l.lobbyId === "10977524123456789");
    expect(lobby).toBeDefined();
    expect(lobby!.players).toBe(7);
    expect(lobby!.capacity).toBe(12);
    expect(lobby!.recentObservers).toBe(2);
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