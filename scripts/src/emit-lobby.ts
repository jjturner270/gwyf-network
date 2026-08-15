import { randomUUID } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import {
  generateObserverKeyPair,
  signBytes,
  type LobbySighting,
  type ObserverKeyPair,
} from "@gwyf/protocol";

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));
const KEY_DIR = join(SCRIPT_DIR, "..", ".keys");
const API_URL = process.env.API_URL ?? "http://localhost:3000";

const args = process.argv.slice(2);
function argValue(name: string, fallback: string): string {
  const idx = args.indexOf(name);
  return idx >= 0 && args[idx + 1] ? (args[idx + 1] ?? fallback) : fallback;
}

const INTERVAL_MS = Number(argValue("--interval", "4000"));
const DURATION_MS = Number(argValue("--duration", "180000"));
const OBSERVERS = Number(argValue("--observers", "2"));

interface FakeMod {
  name: string;
  keyPair: ObserverKeyPair;
  observerId: string;
  sightingCount: number;
}

function loadOrCreateKeys(label: string): ObserverKeyPair {
  const file = join(KEY_DIR, `${label}.json`);
  try {
    const parsed = JSON.parse(readFileSync(file, "utf8")) as { publicKeyHex: string; privateKeyHex: string };
    if (parsed.publicKeyHex && parsed.privateKeyHex) return parsed;
  } catch {}
  mkdirSync(KEY_DIR, { recursive: true });
  const pair = generateObserverKeyPair();
  writeFileSync(file, JSON.stringify(pair, null, 2));
  return pair;
}

async function modAction<T>(mod: FakeMod, path: string, init: { method: string; body: unknown }): Promise<T> {
  const body = JSON.stringify(init.body);
  const response = await fetch(`${API_URL}${path}`, {
    method: init.method,
    headers: {
      "content-type": "application/json",
      "x-observer-id": mod.observerId,
      "x-observer-signature": signBytes(mod.keyPair, body),
    },
    body,
  });
  if (!response.ok) {
    throw new Error(`${path} -> ${response.status}: ${await response.text()}`);
  }
  return (await response.json()) as T;
}

async function ensureObserver(mod: FakeMod, clientVersion: string): Promise<void> {
  const existing = await fetch(`${API_URL}/v1/observers`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      publicKey: mod.keyPair.publicKeyHex,
      clientVersion,
      protocolVersion: 1,
    }),
  });
  const registered = (await existing.json()) as { observerId?: string };
  if (registered.observerId) {
    mod.observerId = registered.observerId;
  } else {
    throw new Error(`observer registration failed: ${JSON.stringify(registered)}`);
  }
}

interface LobbyState {
  steamLobbyId: string;
  name: string;
  members: number;
  capacity: number;
  map: string;
  mode: string;
}

function makeSighting(
  mod: FakeMod,
  lobby: LobbyState,
  observedAt = new Date().toISOString(),
): LobbySighting {
  return {
    schema: "gwyf.lobby-sighting.v1",
    protocolVersion: 1,
    sightingId: randomUUID(),
    observedAt,
    lobby: {
      steamLobbyId: lobby.steamLobbyId,
      name: lobby.name,
      members: lobby.members,
      capacity: lobby.capacity,
      map: lobby.map,
      mode: lobby.mode,
    },
    observer: {
      installationId: mod.observerId,
      clientVersion: "0.1.0",
      searchScope: "default",
    },
  };
}

function jitter(value: number, amount: number): number {
  return Math.max(1, Math.round(value + (Math.random() - 0.5) * 2 * amount));
}

async function main(): Promise<void> {
  const mods: FakeMod[] = [];
  for (let i = 0; i < OBSERVERS; i++) {
    const keyPair = loadOrCreateKeys(`observer-${i}`);
    const mod: FakeMod = {
      name: `observer-${i}`,
      keyPair,
      observerId: "",
      sightingCount: 0,
    };
    if (i > 0) mod.name += " (second client)";
    await ensureObserver(mod, "0.1.0");
    mods.push(mod);
  }
  console.log(`[emit] ${mods.length} observer(s) active: ${mods.map((m) => m.observerId).join(", ")}`);

  const lobbies: LobbyState[] = [
    { steamLobbyId: "10977524123456789", name: "Fuzzy's Hellhole", members: 7, capacity: 12, map: "THE FUCK HOLE v3", mode: "Classic" },
    { steamLobbyId: "10977524123456790", name: "KubysCZ's Lobby", members: 3, capacity: 12, map: "Museum", mode: "Hockey" },
    { steamLobbyId: "10977524123456791", name: "Tournament Warmup", members: 10, capacity: 12, map: "Workshop Course", mode: "Classic" },
  ];
  const fadeStage1Ms = DURATION_MS / 3;
  const fadeStage2Ms = (2 * DURATION_MS) / 3;

  const emitFor = (mod: FakeMod, lobby: LobbyState): void => {
    mod.sightingCount += 1;
    modAction(mod, "/v1/ingest/lobby-sightings", {
      method: "POST",
      body: makeSighting(mod, { ...lobby, members: jitter(lobby.members, 1) }),
    }).catch((err) => console.error(`[emit][${mod.name}] ingest failed: ${err.message}`));
  };

  const startedAt = Date.now();
  const tick = setInterval(() => {
    const elapsed = Date.now() - startedAt;
    mods.forEach((mod, i) => {
      emitFor(mod, lobbies[0]);
      if (elapsed < fadeStage1Ms) emitFor(mod, lobbies[1]);
      if (elapsed < fadeStage2Ms && i === 0) emitFor(mod, lobbies[2]);
    });
  }, INTERVAL_MS);

  const report = setInterval(async () => {
    try {
      const live = (await fetch(`${API_URL}/v1/lobbies/live`).then((r) => r.json())) as {
        generatedAt: string;
        count: number;
        lobbies: Array<{
          lobbyId: string;
          name: string;
          players: number;
          capacity: number;
          status: string;
          confidence: number;
          lastSeenSecondsAgo: number;
          recentObservers: number;
        }>;
      };
      console.log(`\n[report] ${live.generatedAt} — ${live.count} visible lobbies`);
      for (const lobby of live.lobbies) {
        console.log(
          `  ${lobby.name.padEnd(22)} ${String(lobby.players).padStart(2)}/${lobby.capacity}  ${lobby.status.padEnd(14)} conf=${lobby.confidence}  seen ${lobby.lastSeenSecondsAgo}s ago  observers=${lobby.recentObservers}`,
        );
      }
    } catch (err) {
      console.error(`[report] failed: ${(err as Error).message}`);
    }
  }, Math.max(INTERVAL_MS, 5000));

  setTimeout(() => {
    clearInterval(tick);
    clearInterval(report);
    console.log(`\n[emit] done after ${DURATION_MS}ms`);
    process.exit(0);
  }, DURATION_MS);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});