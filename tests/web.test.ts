import { describe, expect, it } from "vitest";
import {
  DEFAULT_API_BASE_URL,
  LiveNetwork,
  ageMsFrom,
  formatAgo,
  resolveApiBase,
  statusLabel,
  stripTrailingSlashes,
  toCard,
} from "../apps/web/src/app.js";

describe("resolveApiBase", () => {
  it("prefers an explicit override", () => {
    expect(resolveApiBase("  https://api.example.com/  ")).toBe("https://api.example.com");
  });

  it("falls back to the baked window config", () => {
    const previous = (globalThis as Record<string, unknown>).window;
    (globalThis as Record<string, unknown>).window = { GWYF_API_BASE_URL: "https://api.prod.example" };
    try {
      expect(resolveApiBase(undefined)).toBe("https://api.prod.example");
    } finally {
      if (previous === undefined) delete (globalThis as Record<string, unknown>).window;
      else (globalThis as Record<string, unknown>).window = previous;
    }
  });

  it("defaults to loopback", () => {
    expect(resolveApiBase(undefined)).toBe(DEFAULT_API_BASE_URL);
  });

  it("strips trailing slashes", () => {
    expect(stripTrailingSlashes("http://127.0.0.1:3000///")).toBe("http://127.0.0.1:3000");
  });
});

describe("statusLabel", () => {
  it("maps server statuses to readable badges", () => {
    expect(statusLabel("active")).toBe("LIVE");
    expect(statusLabel("likely_active")).toBe("LIKELY ACTIVE");
    expect(statusLabel("stale")).toBe("STALE");
  });

  it("never renders expired or unknown as cards", () => {
    expect(statusLabel("expired")).toBeNull();
    expect(statusLabel("unknown")).toBeNull();
  });
});

describe("formatAgo", () => {
  it("formats the freshness language", () => {
    expect(formatAgo(0)).toBe("just now");
    expect(formatAgo(4_000)).toBe("4s ago");
    expect(formatAgo(90_000)).toBe("1m ago");
    expect(formatAgo(7_200_000)).toBe("2h ago");
  });

  it("handles bad input", () => {
    expect(formatAgo(-5)).toBe("");
    expect(formatAgo(Number.NaN)).toBe("");
  });
});

describe("ageMsFrom", () => {
  it("clamps to zero and rejects garbage", () => {
    expect(ageMsFrom("2026-08-15T06:20:00.000Z", Date.parse("2026-08-15T06:20:03.000Z"))).toBe(3000);
    expect(ageMsFrom("2026-08-15T06:20:05.000Z", Date.parse("2026-08-15T06:20:03.000Z"))).toBe(0);
    expect(ageMsFrom(null, Date.now())).toBeNull();
    expect(ageMsFrom("not-a-date", Date.now())).toBeNull();
  });
});

describe("toCard", () => {
  const now = Date.parse("2026-08-15T06:20:00.000Z");

  it("maps a live lobby to its display card", () => {
    const card = toCard(
      {
        lobbyId: "id-1",
        name: "[ULX] Megiddo",
        players: 6,
        capacity: 12,
        map: { name: "Pirate Cove" },
        mode: "Party",
        status: "active",
        observerCount: 3,
        lastSeenAt: new Date(now - 4_000).toISOString(),
      },
      now,
    );
    expect(card).toMatchObject({
      id: "id-1",
      name: "[ULX] Megiddo",
      map: "Pirate Cove",
      mode: "Party",
      players: 6,
      capacity: 12,
      status: "LIVE",
      ago: "4s ago",
      observerCount: 3,
    });
  });

  it("defaults gracefully over missing fields", () => {
    const card = toCard({ lobbyId: "id-2", players: 1 }, now);
    expect(card.name).toBe("Unnamed lobby");
    expect(card.map).toBe("Unknown map");
    expect(card.mode).toBe("Unknown mode");
    expect(card.capacity).toBe(0);
    expect(card.status).toBeNull();
    expect(card.ago).toBeNull();
    expect(card.observerCount).toBe(0);
  });
});

function jsonFetch(payload: unknown, ok = true, status = 200) {
  return async () => ({ ok, status, json: async () => payload });
}

describe("LiveNetwork", () => {
  const NOW = Date.parse("2026-08-15T06:20:00.000Z");

  it("polls live and surfaces online state with lobbies", async () => {
    let state: any;
    let error: unknown = null;
    const network = new LiveNetwork({
      fetchImpl: jsonFetch({
        generatedAt: new Date(NOW).toISOString(),
        count: 1,
        activeObservers: 3,
        lobbies: [
          {
            lobbyId: "id-1",
            name: "[ULX] Megiddo",
            players: 6,
            capacity: 12,
            map: { name: "Pirate Cove" },
            mode: "Party",
            status: "active",
            observerCount: 3,
            lastSeenAt: new Date(NOW - 4_000).toISOString(),
          },
        ],
      }),
      onUpdate: (s: any) => (state = s),
      onError: (e: unknown) => (error = e),
      clock: () => NOW,
    });
    await network.refresh();

    expect(error).toBeNull();
    expect(state.online).toBe(true);
    expect(state.activeObservers).toBe(3);
    expect(state.ageMs).toBe(0);
    expect(state.lobbies).toHaveLength(1);
    expect(state.lobbies[0]).toMatchObject({ name: "[ULX] Megiddo", status: "LIVE", ago: "4s ago" });
  });

  it("distinguishes an empty observation set from a failure", async () => {
    let state: any;
    let error: unknown = null;
    const network = new LiveNetwork({
      fetchImpl: jsonFetch({ generatedAt: new Date(NOW).toISOString(), count: 0, activeObservers: 1, lobbies: [] }),
      onUpdate: (s: any) => (state = s),
      onError: (e: unknown) => (error = e),
      clock: () => NOW,
    });
    await network.refresh();

    expect(error).toBeNull();
    expect(state.online).toBe(true);
    expect(state.lobbies).toEqual([]);
  });

  it("never invents an observer aggregate it did not receive", async () => {
    let state: any;
    const network = new LiveNetwork({
      fetchImpl: jsonFetch({ generatedAt: new Date(NOW).toISOString(), count: 0, lobbies: [] }),
      onUpdate: (s: any) => (state = s),
      onError: () => {},
      clock: () => NOW,
    });
    await network.refresh();

    expect(state.online).toBe(true);
    expect(state.activeObservers).toBeNull();
  });

  it("reports unreachable HTTP responses as an error, never as zero lobbies", async () => {
    let state: any;
    let error: unknown = null;
    const network = new LiveNetwork({
      fetchImpl: jsonFetch({ error: "down" }, false, 503),
      onUpdate: (s: any) => (state = s),
      onError: (e: unknown) => (error = e),
    });
    await network.refresh();

    expect(error).toBeInstanceOf(Error);
    expect((error as Error).message).toBe("HTTP 503");
    expect(state).toBeUndefined();
  });

  it("surfaces transport failures through onError", async () => {
    let state: any;
    let error: unknown = null;
    const network = new LiveNetwork({
      fetchImpl: async () => {
        throw new Error("network down");
      },
      onUpdate: (s: any) => (state = s),
      onError: (e: unknown) => (error = e),
    });
    await network.refresh();

    expect((error as Error).message).toBe("network down");
    expect(state).toBeUndefined();
  });

  it("treats a malformed body as a failure", async () => {
    let error: unknown = null;
    const network = new LiveNetwork({
      fetchImpl: async () => ({ ok: true, status: 200, json: async () => JSON.parse("{nope") }),
      onUpdate: () => {},
      onError: (e: unknown) => (error = e),
    });
    await network.refresh();

    expect(error).toBeInstanceOf(Error);
  });
});