import { sqliteTable, text, integer, index } from "drizzle-orm/sqlite-core";

export const observers = sqliteTable("observers", {
  observerId: text("observer_id").primaryKey(),
  publicKey: text("public_key").notNull().unique(),
  firstSeenAt: integer("first_seen_at").notNull(),
  lastSeenAt: integer("last_seen_at").notNull(),
  clientVersion: text("client_version").notNull(),
  protocolVersion: integer("protocol_version").notNull(),
  status: text("status").notNull().default("active"),
});

export const lobbySightings = sqliteTable(
  "lobby_sightings",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    sightingId: text("sighting_id").notNull().unique(),
    schema: text("schema").notNull(),
    steamLobbyId: text("steam_lobby_id").notNull(),
    observerId: text("observer_id").notNull().references(() => observers.observerId),
    observedAt: integer("observed_at").notNull(),
    receivedAt: integer("received_at").notNull(),
    lobbyName: text("lobby_name"),
    memberCount: integer("member_count").notNull(),
    capacity: integer("capacity").notNull(),
    mapName: text("map_name"),
    gameMode: text("game_mode"),
    searchScope: text("search_scope"),
    rawMetadataJson: text("raw_metadata_json"),
    clientVersion: text("client_version").notNull(),
    gameBuild: text("game_build"),
    signature: text("signature").notNull(),
  },
  (t) => [
    index("idx_lobby_sightings_lobby_observed").on(t.steamLobbyId, t.observedAt),
    index("idx_lobby_sightings_observed").on(t.observedAt),
    index("idx_lobby_sightings_observer").on(t.observerId, t.observedAt),
  ],
);

export const courseSightings = sqliteTable(
  "course_sightings",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    sightingId: text("sighting_id").notNull().unique(),
    schema: text("schema").notNull(),
    fingerprint: text("fingerprint"),
    observerId: text("observer_id").notNull().references(() => observers.observerId),
    observedAt: integer("observed_at").notNull(),
    receivedAt: integer("received_at").notNull(),
    steamLobbyId: text("steam_lobby_id"),
    displayName: text("display_name").notNull(),
    sceneName: text("scene_name").notNull(),
    holeCount: integer("hole_count"),
    objectCount: integer("object_count"),
    metadataJson: text("metadata_json"),
    clientVersion: text("client_version").notNull(),
    signature: text("signature").notNull(),
  },
  (t) => [index("idx_course_sightings_fingerprint_observed").on(t.fingerprint, t.observedAt)],
);

export const runtimeEvents = sqliteTable(
  "runtime_events",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    eventId: text("event_id").notNull().unique(),
    schema: text("schema").notNull(),
    eventType: text("event_type").notNull(),
    observerId: text("observer_id").notNull().references(() => observers.observerId),
    observedAt: integer("observed_at").notNull(),
    receivedAt: integer("received_at").notNull(),
    contextJson: text("context_json"),
    clientVersion: text("client_version").notNull(),
    signature: text("signature").notNull(),
  },
  (t) => [index("idx_runtime_events_observer_observed").on(t.observerId, t.observedAt)],
);