export const PROTOCOL_VERSION = 1;

export const SCHEMA_IDS = {
  lobbySighting: "gwyf.lobby-sighting.v1",
  courseSighting: "gwyf.course-sighting.v1",
  runtimeEvent: "gwyf.runtime-event.v1",
  batch: "gwyf.batch.v1",
} as const;

export const LOBBY_STATUS = {
  active: "active",
  likelyActive: "likely_active",
  stale: "stale",
  expired: "expired",
  unknown: "unknown",
} as const;

export type LobbyStatus = (typeof LOBBY_STATUS)[keyof typeof LOBBY_STATUS];

export const SEARCH_SCOPES = ["default", "worldwide", "region", "invite"] as const;

export const RUNTIME_EVENT_TYPES = [
  "lobby-joined",
  "lobby-exited",
  "map-load-started",
  "map-load-completed",
  "fingerprint-generated",
  "submission-failed",
  "protocol-peer-discovered",
] as const;

export const LOBBY_STATUS_LABELS: Record<LobbyStatus, string> = {
  active: "observed just now",
  likely_active: "observed recently",
  stale: "probably active",
  expired: "no recent observations",
  unknown: "insufficient observations",
};

export const STATUS_PRECEDENCE: Record<LobbyStatus, number> = {
  active: 4,
  likely_active: 3,
  stale: 2,
  expired: 1,
  unknown: 0,
};