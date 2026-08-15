import { describe, expect, it } from "vitest";
import {
  ACTIVE_MAX_AGE_MS,
  deriveLobbyState,
  type LobbySightingRow,
} from "../apps/api/src/services/derive-lobby-state.js";
import { deriveLobbySessions } from "../apps/api/src/services/derive-lobby-sessions.js";
import { LOBBY_STATUS } from "@gwyf/protocol";

const NOW = Date.parse("2026-08-15T06:20:00Z");

function row(
  observedAt: number,
  observerId = "obs_a",
  memberCount = 7,
): LobbySightingRow {
  return {
    steamLobbyId: "10977524123456789",
    observedAt,
    memberCount,
    capacity: 12,
    lobbyName: "Fuzzy's Hellhole",
    mapName: "Custom Map",
    gameMode: "Classic",
    observerId,
  };
}

describe("deriveLobbyState", () => {
  it("reports unknown for an empty sighting set", () => {
    expect(deriveLobbyState([], NOW)).toBeNull();
  });

  it("marks a lobby active when observed within 15 seconds", () => {
    const state = deriveLobbyState([row(NOW - 5_000)], NOW)!;
    expect(state.status).toBe(LOBBY_STATUS.active);
    expect(state.lastSeenSecondsAgo).toBe(5);
  });

  it("marks a lobby active with two observers within 45 seconds", () => {
    const state = deriveLobbyState(
      [row(NOW - 40_000, "obs_a"), row(NOW - 30_000, "obs_b")],
      NOW,
    )!;
    expect(state.status).toBe(LOBBY_STATUS.active);
  });

  it("does not promote a single observer past likely_active at 30s", () => {
    const state = deriveLobbyState([row(NOW - 30_000, "obs_a")], NOW)!;
    expect(state.status).toBe(LOBBY_STATUS.likelyActive);
  });

  it("marks stale within five minutes", () => {
    const state = deriveLobbyState([row(NOW - 200_000)], NOW)!;
    expect(state.status).toBe(LOBBY_STATUS.stale);
  });

  it("marks expired past five minutes", () => {
    const state = deriveLobbyState([row(NOW - 400_000)], NOW)!;
    expect(state.status).toBe(LOBBY_STATUS.expired);
  });

  it("gives a lone observer lower confidence than corroborated observers", () => {
    const single = deriveLobbyState([row(NOW - 5_000, "obs_a")], NOW)!;
    const paired = deriveLobbyState(
      [row(NOW - 5_000, "obs_a"), row(NOW - 4_000, "obs_b")],
      NOW,
    )!;
    expect(paired.confidence).toBeGreaterThan(single.confidence);
  });

  it("caps corroboration credit so many sightings from one observer stay low", () => {
    const manyFromOne = Array.from({ length: 20 }, (_, i) =>
      row(NOW - 1_000 - i * 1_000, "obs_a"),
    );
    const oneFromMany = [
      row(NOW - 5_000, "obs_a"),
      row(NOW - 4_000, "obs_b"),
      row(NOW - 3_000, "obs_c"),
    ];
    expect(deriveLobbyState(manyFromOne, NOW)!.confidence).toBeLessThanOrEqual(
      deriveLobbyState(oneFromMany, NOW)!.confidence + 0.2,
    );
  });

  it("does not regress lastSeen when an older sighting arrives late", () => {
    const fresh = deriveLobbyState([row(NOW - 5_000, "obs_a")], NOW)!;
    const withLate = deriveLobbyState(
      [row(NOW - 5_000, "obs_a"), row(NOW - 15 * 60_000, "obs_b")],
      NOW,
    )!;
    expect(withLate.lastSeenAt).toBe(fresh.lastSeenAt);
    expect(withLate.lastSeenSecondsAgo).toBe(fresh.lastSeenSecondsAgo);
  });

  it("takes the latest sighting as authoritative for display fields", () => {
    const state = deriveLobbyState(
      [row(NOW - 60_000, "obs_a", 2), row(NOW - 5_000, "obs_b", 9)],
      NOW,
    )!;
    expect(state.players).toBe(9);
    expect(state.capacity).toBe(12);
  });

  it("exposes the inputs behind the label", () => {
    const state = deriveLobbyState(
      [row(NOW - 8_000, "obs_a"), row(NOW - 7_000, "obs_b"), row(NOW - 6_000, "obs_c")],
      NOW,
    )!;
    expect(state.inputs.ageMs).toBe(6_000);
    expect(state.inputs.observersWithin45s).toBe(3);
    expect(state.inputs.sightingsWithin60s).toBe(3);
    expect(state.confidence).toBeGreaterThan(0);
  });
});

describe("deriveLobbySessions", () => {
  it("splits sightings on long gaps", () => {
    const sessions = deriveLobbySessions(
      [
        row(NOW - 120 * 60_000),
        row(NOW - 119 * 60_000),
        row(NOW - 60_000),
        row(NOW - 30_000),
      ],
      30 * 60_000,
    );
    expect(sessions).toHaveLength(2);
    expect(sessions[0].sightingCount).toBe(2);
    expect(sessions[1].sightingCount).toBe(2);
    expect(sessions[0].endedAt).not.toBeNull();
    expect(sessions[1].endedAt).toBeNull();
  });

  it("merges frequent sightings into one session", () => {
    const sessions = deriveLobbySessions(
      [row(NOW - 60_000), row(NOW - 30_000), row(NOW - 5_000)],
      30 * 60_000,
    );
    expect(sessions).toHaveLength(1);
    expect(sessions[0].observerCount).toBe(1);
    expect(sessions[0].maxPopulation).toBe(7);
  });
});