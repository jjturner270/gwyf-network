import { z } from "zod";
import { SCHEMA_IDS } from "./constants.js";
import { sightingSchema } from "./sightings.js";

export const batchSchema = z.object({
  schema: z.literal(SCHEMA_IDS.batch),
  protocolVersion: z.number().int().min(1),
  items: z.array(sightingSchema).min(1).max(500),
});

export type Batch = z.infer<typeof batchSchema>;