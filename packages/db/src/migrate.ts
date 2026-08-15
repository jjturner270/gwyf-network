import { readdirSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import Database from "better-sqlite3";

const MIGRATIONS_DIR = join(dirname(fileURLToPath(import.meta.url)), "..", "migrations");

export function migrate(sqlite: Database.Database, migrationsDir = MIGRATIONS_DIR): string[] {
  sqlite.exec("PRAGMA foreign_keys = OFF; BEGIN;");
  try {
    sqlite.exec(`CREATE TABLE IF NOT EXISTS schema_migrations (
      version TEXT PRIMARY KEY,
      applied_at INTEGER NOT NULL
    );`);
    const applied = new Set(
      sqlite.prepare("SELECT version FROM schema_migrations").all().map((r: any) => r.version),
    );
    const files = readdirSync(migrationsDir)
      .filter((f) => f.endsWith(".sql"))
      .sort();
    const ran: string[] = [];
    for (const file of files) {
      if (applied.has(file)) continue;
      const sql = readFileSync(join(migrationsDir, file), "utf8");
      sqlite.exec(sql);
      sqlite
        .prepare("INSERT INTO schema_migrations (version, applied_at) VALUES (?, ?)")
        .run(file, Date.now());
      ran.push(file);
    }
    sqlite.exec("COMMIT;");
    return ran;
  } catch (err) {
    sqlite.exec("ROLLBACK;");
    throw err;
  } finally {
    sqlite.exec("PRAGMA foreign_keys = ON;");
  }
}