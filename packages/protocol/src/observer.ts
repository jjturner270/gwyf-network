import { z } from "zod";
import { PROTOCOL_VERSION } from "./constants.js";

export const observerRegistrationSchema = z.object({
  publicKey: z
    .string()
    .regex(/^[0-9a-f]{64}$/)
    .describe("raw ed25519 public key, hex, 32 bytes"),
  clientVersion: z.string().min(1).max(32),
  protocolVersion: z.number().int().min(1).max(PROTOCOL_VERSION),
});

export type ObserverRegistration = z.infer<typeof observerRegistrationSchema>;