import { mkdirSync } from "node:fs";
import { dirname } from "node:path";
import { createClient, migrate } from "@gwyf/db";
import { loadConfig } from "./config.js";
import { buildApp } from "./app.js";

const config = loadConfig();
mkdirSync(dirname(config.databasePath), { recursive: true });
const client = createClient(config.databasePath);
const applied = migrate(client.sqlite);
if (applied.length > 0) console.log(`[api] applied migrations: ${applied.join(", ")}`);

const app = buildApp({ db: client.db, config });

process.on("SIGINT", () => {
  client.close();
  process.exit(0);
});
process.on("SIGTERM", () => {
  client.close();
  process.exit(0);
});

await app.listen({ port: config.port, host: config.host });
const bound = app.server.address();
const boundPort = typeof bound !== "string" && bound !== null ? bound.port : config.port;
console.log(`[api] listening on http://${config.host}:${boundPort}`);
console.log(`[api] database at ${config.databasePath}`);