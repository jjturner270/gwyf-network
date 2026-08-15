import { z } from "zod";
import { PROTOCOL_VERSION, SCHEMA_IDS, SEARCH_SCOPES } from "./constants.js";

const sightingId = z.string().uuid().describe("client-generated dedup id");

const observedAt = z
  .string()
  .datetime({ offset: true })
  .describe("clock of the observer at observation time");

const protocolVersion = z
  .number()
  .int()
  .min(1)
  .max(PROTOCOL_VERSION)
  .describe("wire protocol version understood by the client");

export const observerInfoSchema = z.object({
  installationId: z
    .string()
    .min(8)
    .max(64)
    .describe("server-issued observer id, not a human identity"),
  clientVersion: z.string().min(1).max(32),
  gameBuild: z
    .string()
    .min(1)
    .max(32)
    .optional()
    .describe("client-side game build string reported at observation time"),
  searchScope: z.enum(SEARCH_SCOPES).optional(),
});

export const lobbySightingSchema = z.object({
  schema: z.literal(SCHEMA_IDS.lobbySighting),
  protocolVersion,
  sightingId,
  observedAt,
  lobby: z.object({
    steamLobbyId: z
      .string()
      .min(1)
      .max(64)
      .regex(/^[0-9A-Za-z_-]+$/)
      .describe("external lobby identifier (Steam lobby id or platform room id)"),
    name: z
      .string()
      .min(1)
      .max(64)
      .nullish()
      .describe("host-chosen lobby name; null when the game exposes none"),
    members: z.number().int().min(0).max(128),
    capacity: z.number().int().min(1).max(128),
    map: z
      .string()
      .min(1)
      .max(128)
      .nullish()
      .describe("map/course name; null when unknown or custom-ref only"),
    mode: z
      .string()
      .min(1)
      .max(64)
      .nullish()
      .describe("game mode; null when the game exposes none"),
    rawMetadata: z.record(z.string(), z.unknown()).optional(),
  }),
  observer: observerInfoSchema,
});

export type LobbySighting = z.infer<typeof lobbySightingSchema>;

export const courseSightingSchema = z.object({
  schema: z.literal(SCHEMA_IDS.courseSighting),
  protocolVersion,
  sightingId,
  observedAt,
  lobbyId: z.string().min(1).max(32).optional(),
  course: z.object({
    displayName: z.string().min(1).max(128),
    sceneName: z.string().min(1).max(128),
    fingerprint: z.string().min(1).max(128).optional(),
    holeCount: z.number().int().min(0).max(256).optional(),
    objectCount: z.number().int().min(0).max(1_000_000).optional(),
  }),
  observer: observerInfoSchema,
});

export type CourseSighting = z.infer<typeof courseSightingSchema>;

export const runtimeEventSchema = z.object({
  schema: z.literal(SCHEMA_IDS.runtimeEvent),
  protocolVersion,
  sightingId,
  observedAt,
  eventType: z.enum([
    "lobby-joined",
    "lobby-exited",
    "map-load-started",
    "map-load-completed",
    "fingerprint-generated",
    "submission-failed",
    "protocol-peer-discovered",
  ]),
  context: z.record(z.string(), z.unknown()).optional(),
  observer: observerInfoSchema,
});

export type RuntimeEvent = z.infer<typeof runtimeEventSchema>;

export const sightingSchema = z.discriminatedUnion("schema", [
  lobbySightingSchema,
  courseSightingSchema,
  runtimeEventSchema,
]);

export type Sighting = z.infer<typeof sightingSchema>;