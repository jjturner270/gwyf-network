CREATE TABLE IF NOT EXISTS observers (
  observer_id      TEXT PRIMARY KEY,
  public_key       TEXT NOT NULL UNIQUE,
  first_seen_at    INTEGER NOT NULL,
  last_seen_at     INTEGER NOT NULL,
  client_version   TEXT NOT NULL,
  protocol_version INTEGER NOT NULL,
  status           TEXT NOT NULL DEFAULT 'active'
);

CREATE TABLE IF NOT EXISTS lobby_sightings (
  id               INTEGER PRIMARY KEY AUTOINCREMENT,
  sighting_id      TEXT NOT NULL UNIQUE,
  schema           TEXT NOT NULL,
  steam_lobby_id   TEXT NOT NULL,
  observer_id      TEXT NOT NULL REFERENCES observers(observer_id),
  observed_at      INTEGER NOT NULL,
  received_at      INTEGER NOT NULL,
  lobby_name       TEXT NOT NULL,
  member_count     INTEGER NOT NULL,
  capacity         INTEGER NOT NULL,
  map_name         TEXT NOT NULL,
  game_mode        TEXT NOT NULL,
  search_scope     TEXT,
  raw_metadata_json TEXT,
  client_version   TEXT NOT NULL,
  game_build       TEXT,
  signature        TEXT NOT NULL,
  UNIQUE (sighting_id)
);

CREATE INDEX IF NOT EXISTS idx_lobby_sightings_lobby_observed
  ON lobby_sightings (steam_lobby_id, observed_at);
CREATE INDEX IF NOT EXISTS idx_lobby_sightings_observed
  ON lobby_sightings (observed_at);
CREATE INDEX IF NOT EXISTS idx_lobby_sightings_observer
  ON lobby_sightings (observer_id, observed_at);

CREATE TABLE IF NOT EXISTS course_sightings (
  id               INTEGER PRIMARY KEY AUTOINCREMENT,
  sighting_id      TEXT NOT NULL UNIQUE,
  schema           TEXT NOT NULL,
  fingerprint      TEXT,
  observer_id      TEXT NOT NULL REFERENCES observers(observer_id),
  observed_at      INTEGER NOT NULL,
  received_at      INTEGER NOT NULL,
  steam_lobby_id   TEXT,
  display_name     TEXT NOT NULL,
  scene_name       TEXT NOT NULL,
  hole_count       INTEGER,
  object_count     INTEGER,
  metadata_json    TEXT,
  client_version   TEXT NOT NULL,
  signature        TEXT NOT NULL,
  UNIQUE (sighting_id)
);

CREATE INDEX IF NOT EXISTS idx_course_sightings_fingerprint_observed
  ON course_sightings (fingerprint, observed_at);

CREATE TABLE IF NOT EXISTS runtime_events (
  id               INTEGER PRIMARY KEY AUTOINCREMENT,
  event_id         TEXT NOT NULL UNIQUE,
  schema           TEXT NOT NULL,
  event_type       TEXT NOT NULL,
  observer_id      TEXT NOT NULL REFERENCES observers(observer_id),
  observed_at      INTEGER NOT NULL,
  received_at      INTEGER NOT NULL,
  context_json     TEXT,
  client_version   TEXT NOT NULL,
  signature        TEXT NOT NULL,
  UNIQUE (event_id)
);

CREATE INDEX IF NOT EXISTS idx_runtime_events_observer_observed
  ON runtime_events (observer_id, observed_at);

CREATE TABLE IF NOT EXISTS schema_migrations (
  version  TEXT PRIMARY KEY,
  applied_at INTEGER NOT NULL
);