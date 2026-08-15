import { LOBBY_STATUS, type LobbyStatus } from "@gwyf/protocol";

//
// Freshness semantics — the only clock that matters is the observer's
// `observedAt` timestamp. There are no join/leave or "goodbye" messages:
// a lobby goes live, ages, and expires purely from observation timestamps.
//
//   live    active          newest observation ≤ 15s, or ≥2 observers within 45s
//   live    likely_active   newest observation ≤ 90s (single observer)
//   stale   still visible   newest observation ≤ 5 min
//   expired dropped from    everything older; /v1/lobbies still retains history
//           /v1/lobbies/live
//
export const ACTIVE_MAX_AGE_MS = 15_000;
export const ACTIVE_MIN_OBSERVERS = 2;
export const ACTIVE_CORROBORATION_WINDOW_MS = 45_000;
export const LIKELY_ACTIVE_MAX_AGE_MS = 90_000;
export const STALE_MAX_AGE_MS = 300_000;
export const RECENT_OBSERVER_WINDOW_MS = 45_000;
export const RECENT_SIGHTING_WINDOW_MS = 60_000;

export function deriveFreshness(ageMs: number, recentObservers: number): LobbyStatus {
  if (ageMs <= ACTIVE_MAX_AGE_MS) return LOBBY_STATUS.active;
  if (recentObservers >= ACTIVE_MIN_OBSERVERS && ageMs <= ACTIVE_CORROBORATION_WINDOW_MS)
    return LOBBY_STATUS.active;
  if (ageMs <= LIKELY_ACTIVE_MAX_AGE_MS) return LOBBY_STATUS.likelyActive;
  if (ageMs <= STALE_MAX_AGE_MS) return LOBBY_STATUS.stale;
  return LOBBY_STATUS.expired;
}

export interface LobbySightingRow {
  steamLobbyId: string;
  observedAt: number;
  memberCount: number;
  capacity: number;
  lobbyName: string | null;
  mapName: string | null;
  gameMode: string | null;
  observerId: string;
}

export interface LobbyLiveState {
  lobbyId: string;
  name: string;
  players: number;
  capacity: number;
  mapName: string;
  mode: string;
  firstSeenAt: string;
  lastSeenAt: string;
  lastSeenSecondsAgo: number;
  recentObservers: number;
  observerCount: number;
  recentSightings: number;
  sightingCount: number;
  status: LobbyStatus;
  confidence: number;
  inputs: {
    ageMs: number;
    observersWithin45s: number;
    sightingsWithin60s: number;
    populationCv: number;
  };
}

const clamp01 = (v: number) => Math.min(1, Math.max(0, v));

//
// Deterministic reconciliation rules (multi-observer truth):
//
// 1. Identity is the `steamLobbyId` — every sighting of the same lobby id is
//    one lobby, however many observers report it or however the lobby evolves.
// 2. `players` / `capacity` come from the newest sighting.
// 3. `name` / `map` / `mode` come from the newest sighting that carries a
//    non-null value — a null field never erases a known value (once a value is
//    known, an observer that genuinely can no longer determine it cannot clear
//    it; intentional).
// 4. Ties on `observedAt` break on the smallest observer id in UTF-16 code-unit
//    order (the JS default string comparison) — a defined lexical ordering, so
//    the same input set derives the same lobby regardless of row order or DB
//    return order.
// 5. `lastSeenAt` is the freshest valid observation; `firstSeenAt` the oldest.
// 6. `observerCount` counts distinct observers within the observation window
//    (all retained sightings), not observers who have ever seen the lobby.
//
export function resolveDisplayRows(rows: LobbySightingRow[]): LobbySightingRow[] {
  return [...rows].sort(
    (a, b) =>
      b.observedAt - a.observedAt ||
      (a.observerId < b.observerId ? -1 : a.observerId > b.observerId ? 1 : 0),
  );
}

export function deriveLobbyState(
  rows: LobbySightingRow[],
  now: number,
): LobbyLiveState | null {
  if (rows.length === 0) return null;
  const sorted = [...rows].sort((a, b) => a.observedAt - b.observedAt);
  const first = sorted[0]!;
  const last = sorted[sorted.length - 1]!;
  const ageMs = now - last.observedAt;
  const recentWindowStart = now - RECENT_OBSERVER_WINDOW_MS;
  const recentObservers = new Set(
    rows.filter((r) => r.observedAt >= recentWindowStart).map((r) => r.observerId),
  ).size;
  const sightingsWindowStart = now - RECENT_SIGHTING_WINDOW_MS;
  const recentSightings = rows.filter((r) => r.observedAt >= sightingsWindowStart).length;
  const observerCount = new Set(rows.map((r) => r.observerId)).size;

  const status = deriveFreshness(ageMs, recentObservers);

  const newest = resolveDisplayRows(rows);
  const newestNonNull = (pick: (r: LobbySightingRow) => string | null): string | null => {
    for (const row of newest) {
      const value = pick(row);
      if (value !== null && value !== undefined) return value;
    }
    return null;
  };

  const avg = rows.reduce((s, r) => s + r.memberCount, 0) / rows.length;
  const variance =
    rows.reduce((s, r) => s + (r.memberCount - avg) ** 2, 0) / rows.length;
  const sigma = Math.sqrt(variance);
  const populationCv = avg > 0 ? sigma / avg : 0;

  const ageScore = clamp01(1 - ageMs / STALE_MAX_AGE_MS);
  const corroboration = clamp01(recentObservers / ACTIVE_MIN_OBSERVERS);
  const activity = clamp01(recentSightings / 5);
  const stability = populationCv > 0.5 ? 0.8 : 1;
  const confidence =
    Math.round(clamp01(0.55 * ageScore + 0.25 * corroboration + 0.2 * activity) * stability * 100) /
    100;

  return {
    lobbyId: last.steamLobbyId,
    name: newestNonNull((r) => r.lobbyName) ?? "Unknown",
    players: newest[0]!.memberCount,
    capacity: newest[0]!.capacity,
    mapName: newestNonNull((r) => r.mapName) ?? "Unknown",
    mode: newestNonNull((r) => r.gameMode) ?? "Unknown",
    firstSeenAt: new Date(first.observedAt).toISOString(),
    lastSeenAt: new Date(last.observedAt).toISOString(),
    lastSeenSecondsAgo: Math.max(0, Math.round(ageMs / 1000)),
    recentObservers,
    observerCount,
    recentSightings,
    sightingCount: rows.length,
    status,
    confidence,
    inputs: {
      ageMs: Math.max(0, ageMs),
      observersWithin45s: recentObservers,
      sightingsWithin60s: recentSightings,
      populationCv: Math.round(populationCv * 100) / 100,
    },
  };
}