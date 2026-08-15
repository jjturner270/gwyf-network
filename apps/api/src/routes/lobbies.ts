import type { FastifyInstance } from "fastify";
import { LOBBY_STATUS } from "@gwyf/protocol";
import type { LobbyStatus } from "@gwyf/protocol";
import type { AppDeps } from "../deps.js";
import {
  deriveLobbyState,
  RECENT_OBSERVER_WINDOW_MS,
  STALE_MAX_AGE_MS,
  type LobbyLiveState,
} from "../services/derive-lobby-state.js";
import { deriveLobbySessions } from "../services/derive-lobby-sessions.js";
import { listLobbyIds, lobbyRowsFor, lobbyHistoryRows } from "../services/lobby-repo.js";
import { countActiveObservers } from "../services/observers.js";

const LIVE_WINDOW_MS = STALE_MAX_AGE_MS;

function statusFilter(status?: string): LobbyStatus | undefined {
  const values = new Set(Object.values(LOBBY_STATUS)) as Set<string>;
  return status !== undefined && values.has(status) ? (status as LobbyStatus) : undefined;
}

function applyFilters(states: LobbyLiveState[], query: Record<string, unknown>): LobbyLiveState[] {
  const status = statusFilter(query.status as string | undefined);
  const map = query.map as string | undefined;
  const mode = query.mode as string | undefined;
  const name = query.q as string | undefined;
  const minPlayers = Number(query.minPlayers ?? 0);
  const filtered = states.filter((s) => {
    if (status && s.status !== status) return false;
    if (map && s.mapName.toLowerCase() !== map.toLowerCase()) return false;
    if (mode && s.mode.toLowerCase() !== mode.toLowerCase()) return false;
    if (name && !s.name.toLowerCase().includes(name.toLowerCase())) return false;
    if (s.players < minPlayers) return false;
    return true;
  });
  const sort = query.sort as string | undefined;
  filtered.sort((a, b) => {
    switch (sort) {
      case "population":
        return b.players - a.players || a.lastSeenSecondsAgo - b.lastSeenSecondsAgo;
      case "slots":
        return b.capacity - b.players - (a.capacity - a.players) || a.lastSeenSecondsAgo - b.lastSeenSecondsAgo;
      case "confidence":
        return b.confidence - a.confidence || a.lastSeenSecondsAgo - b.lastSeenSecondsAgo;
      default:
        return a.lastSeenSecondsAgo - b.lastSeenSecondsAgo;
    }
  });
  return filtered;
}

function toResponse(s: LobbyLiveState) {
  return {
    lobbyId: s.lobbyId,
    name: s.name,
    players: s.players,
    capacity: s.capacity,
    map: { name: s.mapName },
    mode: s.mode,
    firstSeenAt: s.firstSeenAt,
    lastSeenAt: s.lastSeenAt,
    lastSeenSecondsAgo: s.lastSeenSecondsAgo,
    recentObservers: s.recentObservers,
    observerCount: s.observerCount,
    recentSightings: s.recentSightings,
    sightingCount: s.sightingCount,
    confidence: s.confidence,
    status: s.status,
  };
}

async function deriveLiveStates(
  deps: AppDeps,
  now: number,
  listingWindowMs: number,
  rowWindowMs: number,
): Promise<LobbyLiveState[]> {
  const ids = await listLobbyIds(deps.db, now - listingWindowMs, 500);
  const rows = await lobbyRowsFor(
    deps.db,
    ids.map((i) => i.steamLobbyId),
    now - rowWindowMs,
  );
  const states: LobbyLiveState[] = [];
  for (const id of ids) {
    const state = deriveLobbyState(rows.get(id.steamLobbyId) ?? [], now);
    if (state) states.push(state);
  }
  return states;
}

export function lobbiesRoutes(app: FastifyInstance, deps: AppDeps): void {
  app.get("/v1/lobbies/live", async (request, reply) => {
    const now = deps.now();
    const states = await deriveLiveStates(deps, now, LIVE_WINDOW_MS, LIVE_WINDOW_MS);
    const filtered = applyFilters(states, request.query as Record<string, unknown>);
    return {
      generatedAt: new Date(now).toISOString(),
      count: filtered.length,
      activeObservers: await countActiveObservers(deps.db, now - RECENT_OBSERVER_WINDOW_MS),
      lobbies: filtered.slice(0, 200).map(toResponse),
    };
  });

  app.get("/v1/lobbies", async (request, reply) => {
    const now = deps.now();
    const query = request.query as Record<string, unknown>;
    const hours = Math.min(Number(query.sinceHours ?? 24), 24 * 7);
    const listingWindowMs = hours * 3_600_000;
    const states = await deriveLiveStates(deps, now, listingWindowMs, listingWindowMs);
    const filtered = applyFilters(states, query);
    return {
      generatedAt: new Date(now).toISOString(),
      sinceHours: hours,
      count: filtered.length,
      lobbies: filtered.slice(0, 500).map(toResponse),
    };
  });

  app.get("/v1/lobbies/:steamLobbyId", async (request, reply) => {
    const now = deps.now();
    const steamLobbyId = (request.params as { steamLobbyId: string }).steamLobbyId;
    const rows = await lobbyHistoryRows(deps.db, steamLobbyId, now - 24 * 3_600_000);
    const state = deriveLobbyState(rows, now);
    if (!state) {
      return reply.code(404).send({ error: "lobby_not_found", lobbyId: steamLobbyId });
    }
    return {
      lobby: toResponse(state),
    };
  });

  app.get("/v1/lobbies/:steamLobbyId/history", async (request, reply) => {
    const now = deps.now();
    const steamLobbyId = (request.params as { steamLobbyId: string }).steamLobbyId;
    const rows = await lobbyHistoryRows(deps.db, steamLobbyId, now - 24 * 3_600_000);
    if (rows.length === 0) {
      return reply.code(404).send({ error: "lobby_not_found", lobbyId: steamLobbyId });
    }
    return {
      lobbyId: steamLobbyId,
      sessions: deriveLobbySessions(rows).map((s) => ({
        startedAt: s.startedAt,
        lastSeenAt: s.lastSeenAt,
        endedAt: s.endedAt,
        maxPopulation: s.maxPopulation,
        sightingCount: s.sightingCount,
        observerCount: s.observerCount,
      })),
    };
  });
}