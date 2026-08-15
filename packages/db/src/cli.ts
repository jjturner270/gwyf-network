import { join } from "node:path";
import { createClient } from "./client.js";
import { migrate } from "./migrate.js";

const defaultPath = process.env.DATABASE_PATH ?? join(process.cwd(), "data", "gwyf.db");
const client = createClient(defaultPath);
const ran = migrate(client.sqlite);
client.close();
console.log(`[db] applied: ${ran.join(", ") || "(none)"} database at ${defaultPath}`);