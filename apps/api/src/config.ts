export interface ApiConfig {
  port: number;
  host: string;
  databasePath: string;
  ingestRatePerMinute: number;
  clockSkewMs: number;
  ingestMaxAgeMs: number;
}

export function loadConfig(env: NodeJS.ProcessEnv = process.env): ApiConfig {
  return {
    port: Number(env.PORT ?? 3000),
    host: env.HOST ?? "0.0.0.0",
    databasePath: env.DATABASE_PATH ?? "data/gwyf.db",
    ingestRatePerMinute: Number(env.INGEST_RATE_PER_MINUTE ?? 240),
    clockSkewMs: Number(env.INGEST_CLOCK_SKEW_MS ?? 60_000),
    ingestMaxAgeMs: Number(env.INGEST_MAX_AGE_MS ?? 7 * 24 * 3_600_000),
  };
}