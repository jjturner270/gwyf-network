import Fastify, { type FastifyInstance } from "fastify";
import type { GwyfDb } from "@gwyf/db";
import type { ApiConfig } from "./config.js";
import { buildDeps, type AppDeps } from "./deps.js";
import { observersRoutes } from "./routes/observers.js";
import { ingestRoutes } from "./routes/ingest.js";
import { lobbiesRoutes } from "./routes/lobbies.js";

export interface BuildAppOptions {
  db: GwyfDb;
  config: ApiConfig;
  onDeps?: (deps: AppDeps) => void;
}

export function buildApp(options: BuildAppOptions): FastifyInstance {
  const { db, config } = options;
  const deps = buildDeps(db, config);
  options.onDeps?.(deps);

  const app = Fastify({ logger: false });

  app.addContentTypeParser("application/json", { parseAs: "buffer" }, (request, body, done) => {
    try {
      const raw = body as Buffer;
      (request as { rawBody?: Buffer }).rawBody = raw;
      done(null, JSON.parse(raw.toString("utf8")));
    } catch (err) {
      done(err as Error);
    }
  });

  app.get("/healthz", async () => ({ status: "ok" }));

  app.get("/", async () => ({
    service: "gwyf-community",
    version: "0.1.0",
    endpoints: [
      "POST /v1/observers",
      "POST /v1/ingest/lobby-sightings",
      "POST /v1/ingest/course-sightings",
      "POST /v1/ingest/events",
      "POST /v1/ingest/batch",
      "GET /v1/lobbies/live",
      "GET /v1/lobbies",
      "GET /v1/lobbies/:steamLobbyId",
      "GET /v1/lobbies/:steamLobbyId/history",
    ],
  }));

  observersRoutes(app, deps);
  ingestRoutes(app, deps);
  lobbiesRoutes(app, deps);

  return app;
}