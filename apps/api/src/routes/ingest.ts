import type { FastifyInstance, FastifyRequest } from "fastify";
import { z } from "zod";
import {
  batchSchema,
  courseSightingSchema,
  lobbySightingSchema,
  runtimeEventSchema,
} from "@gwyf/protocol";
import type { Sighting } from "@gwyf/protocol";
import type { AppDeps } from "../deps.js";
import { requireObserverAuth } from "../http/observer-auth.js";
import { observeActivity } from "../services/observers.js";

function parseBody<T>(raw: unknown, schema: z.ZodType<T>): T | null {
  const result = schema.safeParse(raw);
  return result.success ? result.data : null;
}

export function ingestRoutes(app: FastifyInstance, deps: AppDeps): void {
  const pre = async (request: FastifyRequest, reply: any) => requireObserverAuth(deps, request, reply);

  app.post("/v1/ingest/lobby-sightings", { preHandler: pre }, async (request, reply) => {
    const item = parseBody(request.body, lobbySightingSchema);
    if (!item) return reply.code(422).send({ error: "invalid_lobby_sighting" });
    const result = await deps.ingest.appendMany([item], (request.headers["x-observer-signature"] as string) ?? "", deps.now());
    await observeActivity(deps.db, item.observer.installationId, deps.now());
    if (result.rejected > 0) {
      return reply.code(422).send({ error: "invalid_sighting", reason: result.items[0]?.reason });
    }
    return reply.code(202).send({
      status: result.accepted === 1 ? "accepted" : "duplicate",
    });
  });

  app.post("/v1/ingest/course-sightings", { preHandler: pre }, async (request, reply) => {
    const item = parseBody(request.body, courseSightingSchema);
    if (!item) return reply.code(422).send({ error: "invalid_course_sighting" });
    const result = await deps.ingest.appendMany([item], (request.headers["x-observer-signature"] as string) ?? "", deps.now());
    await observeActivity(deps.db, item.observer.installationId, deps.now());
    if (result.rejected > 0) {
      return reply.code(422).send({ error: "invalid_sighting", reason: result.items[0]?.reason });
    }
    return reply.code(202).send({
      status: result.accepted === 1 ? "accepted" : "duplicate",
    });
  });

  app.post("/v1/ingest/events", { preHandler: pre }, async (request, reply) => {
    const item = parseBody(request.body, runtimeEventSchema);
    if (!item) return reply.code(422).send({ error: "invalid_runtime_event" });
    const result = await deps.ingest.appendMany([item], (request.headers["x-observer-signature"] as string) ?? "", deps.now());
    await observeActivity(deps.db, item.observer.installationId, deps.now());
    if (result.rejected > 0) {
      return reply.code(422).send({ error: "invalid_event", reason: result.items[0]?.reason });
    }
    return reply.code(202).send({
      status: result.accepted === 1 ? "accepted" : "duplicate",
    });
  });

  app.post("/v1/ingest/batch", { preHandler: pre }, async (request, reply) => {
    const parsed = batchSchema.safeParse(request.body);
    if (!parsed.success) {
      return reply.code(422).send({ error: "invalid_batch", details: parsed.error.flatten() });
    }
    const items = parsed.data.items as Sighting[];
    const signature = (request.headers["x-observer-signature"] as string) ?? "";
    const result = await deps.ingest.appendMany(items, signature, deps.now());
    const observerId = items[0]?.observer.installationId;
    if (observerId) await observeActivity(deps.db, observerId, deps.now());
    return reply.code(202).send({
      accepted: result.accepted,
      duplicates: result.duplicates,
      rejected: result.rejected,
      items: result.items,
    });
  });
}