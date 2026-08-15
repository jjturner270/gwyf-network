import Database from "better-sqlite3";
import { drizzle } from "drizzle-orm/better-sqlite3";
import * as schema from "./schema.js";

export type GwyfDb = ReturnType<typeof createClient>["db"];
export type Sqlite = Database.Database;

export interface DbClient {
  db: ReturnType<typeof drizzle<typeof schema>>;
  sqlite: Sqlite;
  close: () => void;
}

export function createClient(databasePath: string): DbClient {
  const sqlite = new Database(databasePath);
  sqlite.pragma("journal_mode = WAL");
  sqlite.pragma("synchronous = NORMAL");
  sqlite.pragma("busy_timeout = 5000");
  sqlite.pragma("foreign_keys = ON");
  const db = drizzle(sqlite, { schema });
  return {
    db,
    sqlite,
    close: () => sqlite.close(),
  };
}