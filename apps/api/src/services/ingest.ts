import { courseSightings, lobbySightings, runtimeEvents } from "@gwyf/db";
import type { GwyfDb } from "@gwyf/db";
import type {
  CourseSighting,
  LobbySighting,
  RuntimeEvent,
  Sighting,
} from "@gwyf/protocol";

export type ItemAction = "accepted" | "duplicate" | "rejected";

export interface IngestItemResult {
  index: number;
  action: ItemAction;
  schema: string;
  reason?: string;
}

export interface IngestResult {
  accepted: number;
  duplicates: number;
  rejected: number;
  items: IngestItemResult[];
}

function parseObservedAt(iso: string): number | null {
  const ms = Date.parse(iso);
  return Number.isNaN(ms) ? null : ms;
}

export function withinClockSkew(observedAtMs: number, receivedAt: number, skewMs: number): boolean {
  return observedAtMs <= receivedAt + skewMs;
}

export class IngestService {
  constructor(
    private db: GwyfDb,
    private clockSkewMs: number,
  ) {}

  async appendMany(
    items: Sighting[],
    signature: string,
    receivedAt = Date.now(),
  ): Promise<IngestResult> {
    const results: IngestItemResult[] = [];
    for (let i = 0; i < items.length; i++) {
      const item = items[i]!;
      const observedAt = parseObservedAt(item.observedAt);
      if (observedAt === null) {
        results.push({ index: i, action: "rejected", schema: item.schema, reason: "invalid observedAt" });
        continue;
      }
      if (!withinClockSkew(observedAt, receivedAt, this.clockSkewMs)) {
        results.push({ index: i, action: "rejected", schema: item.schema, reason: "observedAt in future beyond clock skew" });
        continue;
      }
      switch (item.schema) {
        case "gwyf.lobby-sighting.v1":
          results.push(await this.appendLobbySighting(item, observedAt, receivedAt, signature, i));
          break;
        case "gwyf.course-sighting.v1":
          results.push(await this.appendCourseSighting(item, observedAt, receivedAt, signature, i));
          break;
        case "gwyf.runtime-event.v1":
          results.push(await this.appendRuntimeEvent(item, observedAt, receivedAt, signature, i));
          break;
      }
    }
    const accepted = results.filter((r) => r.action === "accepted").length;
    const duplicates = results.filter((r) => r.action === "duplicate").length;
    const rejected = results.length - accepted - duplicates;
    return { accepted, duplicates, rejected, items: results };
  }

  private async appendLobbySighting(
    item: LobbySighting,
    observedAt: number,
    receivedAt: number,
    signature: string,
    index: number,
  ): Promise<IngestItemResult> {
    const inserted = await this.db
      .insert(lobbySightings)
      .values({
        sightingId: item.sightingId,
        schema: item.schema,
        steamLobbyId: item.lobby.steamLobbyId,
        observerId: item.observer.installationId,
        observedAt,
        receivedAt,
        lobbyName: item.lobby.name ?? null,
        memberCount: item.lobby.members,
        capacity: item.lobby.capacity,
        mapName: item.lobby.map ?? null,
        gameMode: item.lobby.mode ?? null,
        searchScope: item.observer.searchScope ?? null,
        rawMetadataJson: item.lobby.rawMetadata ? JSON.stringify(item.lobby.rawMetadata) : null,
        clientVersion: item.observer.clientVersion,
        gameBuild: item.observer.gameBuild ?? null,
        signature,
      })
      .onConflictDoNothing({ target: lobbySightings.sightingId })
      .returning({ sightingId: lobbySightings.sightingId });
    const action: ItemAction = inserted.length > 0 ? "accepted" : "duplicate";
    return { index, action, schema: item.schema };
  }

  private async appendCourseSighting(
    item: CourseSighting,
    observedAt: number,
    receivedAt: number,
    signature: string,
    index: number,
  ): Promise<IngestItemResult> {
    const inserted = await this.db
      .insert(courseSightings)
      .values({
        sightingId: item.sightingId,
        schema: item.schema,
        fingerprint: item.course.fingerprint ?? null,
        observerId: item.observer.installationId,
        observedAt,
        receivedAt,
        steamLobbyId: item.lobbyId ?? null,
        displayName: item.course.displayName,
        sceneName: item.course.sceneName,
        holeCount: item.course.holeCount ?? null,
        objectCount: item.course.objectCount ?? null,
        metadataJson: null,
        clientVersion: item.observer.clientVersion,
        signature,
      })
      .onConflictDoNothing({ target: courseSightings.sightingId })
      .returning({ sightingId: courseSightings.sightingId });
    const action: ItemAction = inserted.length > 0 ? "accepted" : "duplicate";
    return { index, action, schema: item.schema };
  }

  private async appendRuntimeEvent(
    item: RuntimeEvent,
    observedAt: number,
    receivedAt: number,
    signature: string,
    index: number,
  ): Promise<IngestItemResult> {
    const inserted = await this.db
      .insert(runtimeEvents)
      .values({
        eventId: item.sightingId,
        schema: item.schema,
        eventType: item.eventType,
        observerId: item.observer.installationId,
        observedAt,
        receivedAt,
        contextJson: item.context ? JSON.stringify(item.context) : null,
        clientVersion: item.observer.clientVersion,
        signature,
      })
      .onConflictDoNothing({ target: runtimeEvents.eventId })
      .returning({ eventId: runtimeEvents.eventId });
    const action: ItemAction = inserted.length > 0 ? "accepted" : "duplicate";
    return { index, action, schema: item.schema };
  }
}