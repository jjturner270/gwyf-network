import type { LobbySightingRow } from "./derive-lobby-state.js";

export const SESSION_GAP_MS = 30 * 60_000;

export interface LobbySession {
  startedAt: string;
  lastSeenAt: string;
  endedAt: string | null;
  maxPopulation: number;
  sightingCount: number;
  observerCount: number;
}

export function deriveLobbySessions(
  rows: LobbySightingRow[],
  gapMs = SESSION_GAP_MS,
): LobbySession[] {
  const sorted = [...rows].sort((a, b) => a.observedAt - b.observedAt);
  const sessions: LobbySession[] = [];
  let current: LobbySightingRow[] = [];
  for (const row of sorted) {
    if (current.length > 0) {
      const prev = current[current.length - 1]!;
      if (row.observedAt - prev.observedAt > gapMs) {
        sessions.push(buildSession(current, true));
        current = [];
      }
    }
    current.push(row);
  }
  if (current.length > 0) sessions.push(buildSession(current, false));
  return sessions;
}

function buildSession(rows: LobbySightingRow[], ended: boolean): LobbySession {
  const last = rows[rows.length - 1]!;
  return {
    startedAt: new Date(rows[0]!.observedAt).toISOString(),
    lastSeenAt: new Date(last.observedAt).toISOString(),
    endedAt: ended ? new Date(last.observedAt).toISOString() : null,
    maxPopulation: Math.max(...rows.map((r) => r.memberCount)),
    sightingCount: rows.length,
    observerCount: new Set(rows.map((r) => r.observerId)).size,
  };
}