-- 0001: lobby_name / map_name / game_mode become nullable.
-- The real game hook (IL2CPP build 256) cannot always provide a lobby host
-- name, course name or mode string; empty values must persist as NULL instead
-- of blocking the sighting. SQLite cannot drop NOT NULL via ALTER, so the
-- table is rebuilt inside the runner's transaction (foreign_keys are already
-- OFF there) and the indexes are recreated.

CREATE TABLE lobby_sightings_new (
  id                INTEGER PRIMARY KEY AUTOINCREMENT,
  sighting_id       TEXT NOT NULL UNIQUE,
  schema            TEXT NOT NULL,
  steam_lobby_id    TEXT NOT NULL,
  observer_id       TEXT NOT NULL REFERENCES observers(observer_id),
  observed_at       INTEGER NOT NULL,
  received_at       INTEGER NOT NULL,
  lobby_name        TEXT,
  member_count      INTEGER NOT NULL,
  capacity          INTEGER NOT NULL,
  map_name          TEXT,
  game_mode         TEXT,
  search_scope      TEXT,
  raw_metadata_json TEXT,
  client_version    TEXT NOT NULL,
  game_build        TEXT,
  signature         TEXT NOT NULL,
  UNIQUE (sighting_id)
);

INSERT INTO lobby_sightings_new (
  id, sighting_id, schema, steam_lobby_id, observer_id, observed_at,
  received_at, lobby_name, member_count, capacity, map_name, game_mode,
  search_scope, raw_metadata_json, client_version, game_build, signature
)
SELECT id, sighting_id, schema, steam_lobby_id, observer_id, observed_at,
       received_at, lobby_name, member_count, capacity, map_name, game_mode,
       search_scope, raw_metadata_json, client_version, game_build, signature
FROM lobby_sightings;

DROP TABLE lobby_sightings;
ALTER TABLE lobby_sightings_new RENAME TO lobby_sightings;

CREATE INDEX idx_lobby_sightings_lobby_observed
  ON lobby_sightings (steam_lobby_id, observed_at);
CREATE INDEX idx_lobby_sightings_observed
  ON lobby_sightings (observed_at);
CREATE INDEX idx_lobby_sightings_observer
  ON lobby_sightings (observer_id, observed_at);