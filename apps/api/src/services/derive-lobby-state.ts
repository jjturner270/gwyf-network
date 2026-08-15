import { LOBBY_STATUS, type LobbyStatus } from "@gwyf/protocol";

export const ACTIVE_MAX_AGE_MS = 15_000;
export const ACTIVE_MIN_OBSERVERS = 2;
export const ACTIVE_CORROBORATION_WINDOW_MS = 45_000;
export const LIKELY_ACTIVE_MAX_AGE_MS = 90_000;
export const STALE_MAX_AGE_MS = 300_000;
export const RECENT_OBSERVER_WINDOW_MS = 45_000;
export const RECENT_SIGHTING_WINDOW_MS = 60_000;

export interface LobbySightingRow {
  steamLobbyId: string;
  observedAt: number;
  memberCount: number;
  capacity: number;
  lobbyName: string;
  mapName: string;
  gameMode: string;
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

  let status: LobbyStatus;
  if (ageMs <= ACTIVE_MAX_AGE_MS) status = LOBBY_STATUS.active;
  else if (
    recentObservers >= ACTIVE_MIN_OBSERVERS &&
    ageMs <= ACTIVE_CORROBORATION_WINDOW_MS
  )
    status = LOBBY_STATUS.active;
  else if (ageMs <= LIKELY_ACTIVE_MAX_AGE_MS) status = LOBBY_STATUS.likelyActive;
  else if (ageMs <= STALE_MAX_AGE_MS) status = LOBBY_STATUS.stale;
  else status = LOBBY_STATUS.expired;

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
    name: last.lobbyName,
    players: last.memberCount,
    capacity: last.capacity,
    mapName: last.mapName,
    mode: last.gameMode,
    firstSeenAt: new Date(first.observedAt).toISOString(),
    lastSeenAt: new Date(last.observedAt).toISOString(),
    lastSeenSecondsAgo: Math.max(0, Math.round(ageMs / 1000)),
    recentObservers,
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