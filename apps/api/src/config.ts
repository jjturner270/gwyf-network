export interface ApiConfig {
  port: number;
  host: string;
  databasePath: string;
  ingestRatePerMinute: number;
  clockSkewMs: number;
  ingestMaxAgeMs: number;
  minimumClientVersion: string;
  latestClientVersion: string;
  supportedGameBuilds: number[];
  ingestEnabled: boolean;
  configVersion: number;
}

export function loadConfig(env: NodeJS.ProcessEnv = process.env): ApiConfig {
  return {
    port: Number(env.PORT ?? 3000),
    host: env.HOST ?? "0.0.0.0",
    databasePath: env.DATABASE_PATH ?? "data/gwyf.db",
    ingestRatePerMinute: Number(env.INGEST_RATE_PER_MINUTE ?? 240),
    clockSkewMs: Number(env.INGEST_CLOCK_SKEW_MS ?? 60_000),
    ingestMaxAgeMs: Number(env.INGEST_MAX_AGE_MS ?? 7 * 24 * 3_600_000),
    minimumClientVersion: env.CLIENT_MIN_VERSION ?? "0.3.0",
    latestClientVersion: env.CLIENT_LATEST_VERSION ?? "0.3.0",
    supportedGameBuilds: parseNumbers(env.CLIENT_SUPPORTED_GAME_BUILDS ?? "303"),
    ingestEnabled: (env.CLIENT_INGEST_ENABLED ?? "true").toLowerCase() !== "false",
    configVersion: Number(env.CLIENT_CONFIG_VERSION ?? 1),
  };
}

function parseNumbers(raw: string): number[] {
  return raw
    .split(",")
    .map((part) => Number(part.trim()))
    .filter((value) => Number.isFinite(value));
}