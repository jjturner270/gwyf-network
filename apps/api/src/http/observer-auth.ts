import type { FastifyReply, FastifyRequest } from "fastify";
import type { AppDeps } from "../deps.js";

export interface RawBodyRequest {
  rawBody?: Buffer;
}

export async function requireObserverAuth(
  deps: AppDeps,
  request: FastifyRequest,
  reply: FastifyReply,
): Promise<boolean> {
  const observerId = request.headers["x-observer-id"] as string | undefined;
  const signature = request.headers["x-observer-signature"] as string | undefined;
  if (!observerId || !signature) {
    reply.code(401).send({ error: "unauthorized", reason: "missing_headers" });
    return false;
  }
  const observer = await deps.getObserver(deps.db, observerId);
  if (!observer) {
    reply.code(401).send({ error: "unauthorized", reason: "unknown_observer" });
    return false;
  }
  if (observer.status !== "active") {
    reply.code(403).send({ error: "forbidden", reason: "observer_suspended" });
    return false;
  }
  const rawBody = (request as unknown as RawBodyRequest).rawBody;
  if (!rawBody || !deps.verifySignature(observer.publicKey, rawBody, signature)) {
    reply.code(401).send({ error: "unauthorized", reason: "bad_signature" });
    return false;
  }
  if (!deps.rateLimiter.allow(observerId, deps.now())) {
    reply.code(429).send({ error: "rate_limited", reason: "ingest rate exceeded" });
    return false;
  }
  return true;
}