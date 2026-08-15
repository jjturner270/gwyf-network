import { and, count, eq, gt } from "drizzle-orm";
import { observers } from "@gwyf/db";
import type { GwyfDb } from "@gwyf/db";
import { deriveObserverId } from "@gwyf/protocol";
import type { ObserverRegistration } from "@gwyf/protocol";

export interface ObserverRecord {
  observerId: string;
  publicKey: string;
  firstSeenAt: number;
  lastSeenAt: number;
  clientVersion: string;
  protocolVersion: number;
  status: string;
}

export async function registerObserver(db: GwyfDb, registration: ObserverRegistration): Promise<void> {
  const observerId = deriveObserverId(registration.publicKey);
  const now = Date.now();
  await db
    .insert(observers)
    .values({
      observerId,
      publicKey: registration.publicKey,
      firstSeenAt: now,
      lastSeenAt: now,
      clientVersion: registration.clientVersion,
      protocolVersion: registration.protocolVersion,
      status: "active",
    })
    .onConflictDoUpdate({
      target: observers.publicKey,
      set: {
        lastSeenAt: now,
        clientVersion: registration.clientVersion,
        protocolVersion: registration.protocolVersion,
      },
    });
}

export async function getObserver(db: GwyfDb, observerId: string): Promise<ObserverRecord | null> {
  const rows = await db.select().from(observers).where(eq(observers.observerId, observerId)).limit(1);
  const row = rows[0];
  if (!row) return null;
  return {
    observerId: row.observerId,
    publicKey: row.publicKey,
    firstSeenAt: row.firstSeenAt,
    lastSeenAt: row.lastSeenAt,
    clientVersion: row.clientVersion,
    protocolVersion: row.protocolVersion,
    status: row.status,
  };
}

export async function observeActivity(db: GwyfDb, observerId: string, now = Date.now()): Promise<void> {
  await db
    .update(observers)
    .set({ lastSeenAt: now })
    .where(and(eq(observers.observerId, observerId)));
}

/**
 * Distinct observers who contributed an observation (or registered) at or after
 * `sinceMs`. Drives the public page's "N ACTIVE OBSERVERS" header line — an
 * honest aggregate, never derived from lobby results.
 */
export async function countActiveObservers(db: GwyfDb, sinceMs: number): Promise<number> {
  const rows = await db
    .select({ value: count() })
    .from(observers)
    .where(and(eq(observers.status, "active"), gt(observers.lastSeenAt, sinceMs)));
  return rows[0]?.value ?? 0;
}