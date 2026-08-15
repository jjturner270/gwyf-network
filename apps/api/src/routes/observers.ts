import type { FastifyInstance } from "fastify";
import { observerRegistrationSchema } from "@gwyf/protocol";
import { deriveObserverId } from "@gwyf/protocol";
import { registerObserver } from "../services/observers.js";
import type { AppDeps } from "../deps.js";

export function observersRoutes(app: FastifyInstance, deps: AppDeps): void {
  app.post("/v1/observers", async (request, reply) => {
    const parsed = observerRegistrationSchema.safeParse(request.body);
    if (!parsed.success) {
      return reply.code(422).send({ error: "invalid_observer_registration", details: parsed.error.flatten() });
    }
    await registerObserver(deps.db, parsed.data);
    return reply.code(201).send({
      observerId: deriveObserverId(parsed.data.publicKey),
      status: "active",
    });
  });
}