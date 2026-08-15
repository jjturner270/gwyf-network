import type { GwyfDb } from "@gwyf/db";
import type { ApiConfig } from "./config.js";
import { IngestService } from "./services/ingest.js";
import { RateLimiter } from "./services/rate-limit.js";
import { getObserver } from "./services/observers.js";
import { verifySignature } from "@gwyf/protocol";

export interface AppDeps {
  db: GwyfDb;
  config: ApiConfig;
  ingest: IngestService;
  rateLimiter: RateLimiter;
  getObserver: typeof getObserver;
  verifySignature: typeof verifySignature;
  now: () => number;
}

export function buildDeps(db: GwyfDb, config: ApiConfig): AppDeps {
  return {
    db,
    config,
    ingest: new IngestService(db, config.clockSkewMs),
    rateLimiter: new RateLimiter(config.ingestRatePerMinute),
    getObserver,
    verifySignature,
    now: Date.now,
  };
}