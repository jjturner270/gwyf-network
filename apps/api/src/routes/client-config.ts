import type { FastifyInstance } from "fastify";
import type { AppDeps } from "../deps.js";

/**
 * Compatibility contract a client fetches before it starts observing. Same
 * origin as ingest, unauthenticated (the client needs it before it has an
 * observer identity). Fail-closed decisions are made client-side against this
 * contract; the server never redefines how clients interpret the game.
 */
export function clientConfigRoutes(app: FastifyInstance, deps: AppDeps): void {
  app.get("/v1/client/config", async (_request, reply) => {
    reply.header("cache-control", "no-store, no-cache, must-revalidate");
    return clientConfig(deps.config);
  });
}

export function clientConfig(config: { minimumClientVersion: string; latestClientVersion: string; supportedGameBuilds: number[]; ingestEnabled: boolean; configVersion: number }) {
  return {
    protocolVersion: 1,
    configVersion: config.configVersion,
    minimumClientVersion: config.minimumClientVersion,
    latestClientVersion: config.latestClientVersion,
    supportedGameBuilds: config.supportedGameBuilds,
    ingestEnabled: config.ingestEnabled,
  };
}