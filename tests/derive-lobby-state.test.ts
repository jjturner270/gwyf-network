import { describe, expect, it } from "vitest";
import {
  ACTIVE_MAX_AGE_MS,
  deriveFreshness,
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

describe("deriveFreshness (formalized observation-timestamp semantics)", () => {
  it("is live while observed within the active window", () => {
    expect(deriveFreshness(0, 1)).toBe(LOBBY_STATUS.active);
    expect(deriveFreshness(ACTIVE_MAX_AGE_MS, 1)).toBe(LOBBY_STATUS.active);
  });

  it("promotes a corroborated lobby past the single-observer live window", () => {
    expect(deriveFreshness(44_000, 2)).toBe(LOBBY_STATUS.active);
    expect(deriveFreshness(44_000, 1)).toBe(LOBBY_STATUS.likelyActive);
  });

  it("decays live -> stale -> expired from age alone", () => {
    expect(deriveFreshness(89_000, 1)).toBe(LOBBY_STATUS.likelyActive);
    expect(deriveFreshness(200_000, 1)).toBe(LOBBY_STATUS.stale);
    expect(deriveFreshness(301_000, 1)).toBe(LOBBY_STATUS.expired);
  });

  it("an observer going silent without any goodbye decays the lobby", () => {
    expect(deriveFreshness(30_000, 0)).toBe(LOBBY_STATUS.likelyActive);
    expect(deriveFreshness(200_000, 0)).toBe(LOBBY_STATUS.stale);
    expect(deriveFreshness(400_000, 0)).toBe(LOBBY_STATUS.expired);
  });
});

describe("multi-observer reconciliation", () => {
  it("produces one lobby and counts every observer in the window", () => {
    const state = deriveLobbyState(
      [
        row(NOW - 30_000, "obs_a", 4),
        row(NOW - 20_000, "obs_b", 5),
        row(NOW - 10_000, "obs_c", 6),
      ],
      NOW,
    )!;
    expect(state.observerCount).toBe(3);
    expect(state.sightingCount).toBe(3);
    expect(state.lobbyId).toBe("10977524123456789");
  });

  it("keeps lastSeenAt on the freshest valid observation regardless of who sent it", () => {
    const state = deriveLobbyState(
      [row(NOW - 45_000, "obs_a", 2), row(NOW - 4_000, "obs_b", 9)],
      NOW,
    )!;
    expect(state.lastSeenAt).toBe(new Date(NOW - 4_000).toISOString());
    expect(state.lastSeenSecondsAgo).toBe(4);
  });

  it("resolves metadata deterministically: newest non-null value wins", () => {
    const state = deriveLobbyState(
      [
        { ...row(NOW - 90_000, "obs_a"), lobbyName: "Old Name", mapName: "Forest", gameMode: "Classic" },
        { ...row(NOW - 5_000, "obs_b"), lobbyName: "New Name", mapName: "Desert", gameMode: null },
      ],
      NOW,
    )!;
    expect(state.name).toBe("New Name");
    expect(state.mapName).toBe("Desert");
    expect(state.mode).toBe("Classic");
  });

  it("a null field in a newer sighting never erases a known value", () => {
    const state = deriveLobbyState(
      [
        { ...row(NOW - 10_000, "obs_a"), lobbyName: "Known", mapName: "Forest", gameMode: "Classic" },
        { ...row(NOW - 1_000, "obs_b"), lobbyName: null, mapName: null, gameMode: null },
      ],
      NOW,
    )!;
    expect(state.name).toBe("Known");
    expect(state.mapName).toBe("Forest");
    expect(state.mode).toBe("Classic");
  });

  it("breaks observedAt ties deterministically on observer id", () => {
    const a = { ...row(NOW - 1_000, "obs_aaa"), lobbyName: "From A" };
    const b = { ...row(NOW - 1_000, "obs_bbb"), lobbyName: "From B" };
    const state = deriveLobbyState([a, b], NOW)!;
    expect(state.name).toBe("From A");
    expect(deriveLobbyState([b, a], NOW)!.name).toBe("From A");
  });

  it("uses the newest sighting for players and capacity", () => {
    const state = deriveLobbyState([row(NOW - 60_000, "obs_a", 2), row(NOW - 5_000, "obs_b", 9)], NOW)!;
    expect(state.players).toBe(9);
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