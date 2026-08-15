import { desc, eq, and, gte, inArray } from "drizzle-orm";
import { lobbySightings } from "@gwyf/db";
import type { GwyfDb } from "@gwyf/db";
import type { LobbySightingRow } from "../services/derive-lobby-state.js";

export interface LobbyLatest {
  steamLobbyId: string;
  latestObservedAt: number;
  sightingCount: number;
}

export async function listLobbyIds(
  db: GwyfDb,
  sinceMs: number,
  limit = 500,
): Promise<LobbyLatest[]> {
  const grouped = db.$client
    .prepare(
      `SELECT steam_lobby_id AS steamLobbyId,
              MAX(observed_at) AS latestObservedAt,
              COUNT(*) AS sightingCount
       FROM lobby_sightings
       WHERE observed_at >= ?
       GROUP BY steam_lobby_id
       ORDER BY latestObservedAt DESC
       LIMIT ?`,
    )
    .all(sinceMs, limit) as LobbyLatest[];
  return grouped;
}

export async function lobbyRowsFor(
  db: GwyfDb,
  ids: string[],
  sinceMs: number,
): Promise<Map<string, LobbySightingRow[]>> {
  if (ids.length === 0) return new Map();
  const rows = await db
    .select({
      steamLobbyId: lobbySightings.steamLobbyId,
      observedAt: lobbySightings.observedAt,
      memberCount: lobbySightings.memberCount,
      capacity: lobbySightings.capacity,
      lobbyName: lobbySightings.lobbyName,
      mapName: lobbySightings.mapName,
      gameMode: lobbySightings.gameMode,
      observerId: lobbySightings.observerId,
    })
    .from(lobbySightings)
    .where(and(inArray(lobbySightings.steamLobbyId, ids), gte(lobbySightings.observedAt, sinceMs)))
    .orderBy(lobbySightings.observedAt);
  const grouped = new Map<string, LobbySightingRow[]>();
  for (const row of rows) {
    const list = grouped.get(row.steamLobbyId) ?? [];
    list.push(row);
    grouped.set(row.steamLobbyId, list);
  }
  return grouped;
}

export async function lobbyHistoryRows(
  db: GwyfDb,
  steamLobbyId: string,
  sinceMs: number,
): Promise<LobbySightingRow[]> {
  const rows = await db
    .select({
      steamLobbyId: lobbySightings.steamLobbyId,
      observedAt: lobbySightings.observedAt,
      memberCount: lobbySightings.memberCount,
      capacity: lobbySightings.capacity,
      lobbyName: lobbySightings.lobbyName,
      mapName: lobbySightings.mapName,
      gameMode: lobbySightings.gameMode,
      observerId: lobbySightings.observerId,
    })
    .from(lobbySightings)
    .where(and(eq(lobbySightings.steamLobbyId, steamLobbyId), gte(lobbySightings.observedAt, sinceMs)))
    .orderBy(lobbySightings.observedAt)
    .limit(50_000);
  return rows;
}