import { describe, expect, it } from "vitest";
import {
  PROTOCOL_VERSION,
  SCHEMA_IDS,
  batchSchema,
  courseSightingSchema,
  lobbySightingSchema,
  observerRegistrationSchema,
  runtimeEventSchema,
} from "@gwyf/protocol";

const docExample = {
  schema: "gwyf.lobby-sighting.v1",
  protocolVersion: 1,
  sightingId: "6f1d5b2e-9c8a-4f3e-b0d1-2e4a6c8f0a12",
  observedAt: "2026-08-15T06:12:00Z",
  lobby: {
    steamLobbyId: "10977524123456789",
    name: "Fuzzy's Hellhole",
    members: 7,
    capacity: 12,
    map: "Workshop Course",
    mode: "Classic",
    rawMetadata: {},
  },
  observer: {
    installationId: "obs_7f2a4b9c1d3e5f67890abcdef01234567",
    clientVersion: "0.1.0",
    searchScope: "default",
  },
};

describe("protocol schemas", () => {
  it("accepts the documented lobby sighting example", () => {
    expect(lobbySightingSchema.safeParse(docExample).success).toBe(true);
  });

  it("accepts the documented course sighting example", () => {
    const parsed = courseSightingSchema.safeParse({
      schema: "gwyf.course-sighting.v1",
      protocolVersion: 1,
      sightingId: "9f1d5b2e-9c8a-4f3e-b0d1-2e4a6c8f0a12",
      observedAt: "2026-08-15T06:15:04Z",
      lobbyId: "10977524123456789",
      course: {
        displayName: "THE FUCK HOLE v3",
        sceneName: "CustomLevel",
        fingerprint: "map_sha256_abcd1234",
        objectCount: 1834,
        holeCount: 18,
      },
      observer: { installationId: "obs_7f2a4b9c1d3e5f67890abcdef01234567", clientVersion: "0.1.0" },
    });
    expect(parsed.success).toBe(true);
  });

  it("accepts photon-style room ids (build 256 uses guid-like ids, not numeric)", () => {
    const good = { ...docExample, lobby: { ...docExample.lobby, steamLobbyId: "6052dc1a-2f0e-4d84-b5e3-1d5cd616d419" } };
    expect(lobbySightingSchema.safeParse(good).success).toBe(true);
  });

  it("rejects a steam lobby id with disallowed characters", () => {
    const bad = { ...docExample, lobby: { ...docExample.lobby, steamLobbyId: "room with spaces" } };
    expect(lobbySightingSchema.safeParse(bad).success).toBe(false);
  });

  it("rejects members above lobby capacity bounds", () => {
    const bad = { ...docExample, lobby: { ...docExample.lobby, members: 512 } };
    expect(lobbySightingSchema.safeParse(bad).success).toBe(false);
  });

  it("rejects a protocol version above the current protocol", () => {
    const bad = { ...docExample, protocolVersion: PROTOCOL_VERSION + 1 };
    expect(lobbySightingSchema.safeParse(bad).success).toBe(false);
  });

  it("rejects sighting ids that are not uuids", () => {
    const bad = { ...docExample, sightingId: "not-a-uuid" };
    expect(lobbySightingSchema.safeParse(bad).success).toBe(false);
  });

  it("discriminates batch items by schema", () => {
    const batch = {
      schema: SCHEMA_IDS.batch,
      protocolVersion: 1,
      items: [
        docExample,
        {
          schema: "gwyf.runtime-event.v1",
          protocolVersion: 1,
          sightingId: "a1d5b2e0-9c8a-4f3e-b0d1-2e4a6c8f0a12",
          observedAt: "2026-08-15T06:12:01Z",
          eventType: "lobby-joined",
          observer: { installationId: "obs_7f2a4b9c1d3e5f67890abcdef01234567", clientVersion: "0.1.0" },
        },
      ],
    };
    const parsed = batchSchema.safeParse(batch);
    expect(parsed.success).toBe(true);
    if (parsed.success) {
      expect(parsed.data.items[0].schema).toBe("gwyf.lobby-sighting.v1");
      expect(parsed.data.items[1].schema).toBe("gwyf.runtime-event.v1");
    }
  });

  it("accepts an ed25519 hex public key in observer registration", () => {
    const ok = observerRegistrationSchema.safeParse({
      publicKey: "ab".repeat(32),
      clientVersion: "0.1.0",
      protocolVersion: 1,
    });
    expect(ok.success).toBe(true);
    const bad = observerRegistrationSchema.safeParse({
      publicKey: "zz",
      clientVersion: "0.1.0",
      protocolVersion: 1,
    });
    expect(bad.success).toBe(false);
  });

  it("validates every runtime event type", () => {
    for (const type of ["lobby-joined", "lobby-exited", "map-load-started", "map-load-completed", "fingerprint-generated", "submission-failed", "protocol-peer-discovered"]) {
      const ok = runtimeEventSchema.safeParse({
        schema: "gwyf.runtime-event.v1",
        protocolVersion: 1,
        sightingId: "b2e5d1a0-9c8a-4f3e-b0d1-2e4a6c8f0a12",
        observedAt: "2026-08-15T06:12:01Z",
        eventType: type,
        observer: { installationId: "obs_7f2a4b9c1d3e5f67890abcdef01234567", clientVersion: "0.1.0" },
      });
      expect(ok.success).toBe(true);
    }
  });
});