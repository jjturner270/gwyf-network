import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { spawn, type ChildProcess } from "node:child_process";
import { randomUUID } from "node:crypto";
import { existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import {
  generateObserverKeyPair,
  signBytes,
  type LobbySighting,
} from "@gwyf/protocol";

const REPO_ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const SERVER_ENTRY = join(REPO_ROOT, "apps", "api", "dist", "server.js");

function fail(message: string, child?: ChildProcess): never {
  if (child) {
    child.stdout?.destroy();
    child.stderr?.destroy();
    child.kill();
  }
  throw new Error(message);
}

interface BootedServer {
  child: ChildProcess;
  port: number;
  base: string;
}

async function bootServer(dbPath: string): Promise<BootedServer> {
  const child = spawn(process.execPath, [SERVER_ENTRY], {
    cwd: REPO_ROOT,
    stdio: ["ignore", "pipe", "pipe"],
    env: { ...process.env, PORT: "0", HOST: "127.0.0.1", DATABASE_PATH: dbPath },
  });
  let stderr = "";
  child.stderr?.on("data", (chunk: Buffer) => (stderr += chunk.toString()));

  const port = await new Promise<number>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`server did not start in 15s\n${stderr}`)), 15_000);
    child.stdout?.on("data", (chunk: Buffer) => {
      const match = chunk.toString().match(/\[api\] listening on http:\/\/127\.0\.0\.1:(\d+)/);
      if (match?.[1]) {
        clearTimeout(timer);
        resolve(Number(match[1]));
      }
    });
    child.on("exit", (code) => reject(new Error(`server exited early (${code})\n${stderr}`)));
  }).catch((err) => fail((err as Error).message, child));

  const base = `http://127.0.0.1:${port}`;
  for (let i = 0; i < 20; i++) {
    if (await fetch(`${base}/healthz`).then((r) => r.ok).catch(() => false)) return { child, port, base };
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  return fail(`healthz never became ready at ${base}`, child);
}

async function getJson<T>(server: BootedServer, path: string): Promise<T> {
  const response = await fetch(`${server.base}${path}`);
  if (!response.ok) fail(`${path} -> ${response.status}`, server.child);
  return (await response.json()) as T;
}

async function stopServer(server: BootedServer): Promise<void> {
  server.child.kill();
  await new Promise<void>((resolve) => {
    if (server.child.exitCode !== null) {
      server.child.stdout?.destroy();
      server.child.stderr?.destroy();
      return resolve();
    }
    const timer = setTimeout(() => {
      server.child.kill();
      server.child.stdout?.destroy();
      server.child.stderr?.destroy();
      resolve();
    }, 3_000);
    server.child.on("exit", () => {
      clearTimeout(timer);
      server.child.stdout?.destroy();
      server.child.stderr?.destroy();
      resolve();
    });
  });
}

interface ConfigContract {
  protocolVersion: number;
  configVersion: number;
  minimumClientVersion: string;
  latestClientVersion: string;
  supportedGameBuilds: number[];
  ingestEnabled: boolean;
}

async function assertConfigContract(server: BootedServer, label: string): Promise<unknown> {
  const config = await getJson<ConfigContract>(server, "/v1/client/config");
  const required: Array<keyof ConfigContract> = [
    "protocolVersion",
    "configVersion",
    "minimumClientVersion",
    "latestClientVersion",
    "supportedGameBuilds",
    "ingestEnabled",
  ];
  for (const key of required) {
    if (!(key in config)) fail(`${label}: config missing "${key}"`, server.child);
  }
  if (config.protocolVersion !== 1) fail(`${label}: protocolVersion expected 1`, server.child);
  if (!Array.isArray(config.supportedGameBuilds)) fail(`${label}: supportedGameBuilds not an array`, server.child);
  console.log(`[build-smoke] ${label}: ${JSON.stringify(config)}`);
  return config;
}

interface ColdClient {
  keyPair: ReturnType<typeof generateObserverKeyPair>;
  observerId: string;
}

async function registerObserver(server: BootedServer): Promise<ColdClient> {
  const keyPair = generateObserverKeyPair();
  const response = await fetch(`${server.base}/v1/observers`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ publicKey: keyPair.publicKeyHex, clientVersion: "0.3.0", protocolVersion: 1 }),
  });
  const body = (await response.json()) as { observerId?: string };
  if (!response.ok || !body.observerId) {
    fail(`observer registration failed: ${response.status} ${JSON.stringify(body)}`, server.child);
  }
  return { keyPair, observerId: body.observerId as string };
}

function makeSighting(client: ColdClient, observedAt: string): LobbySighting {
  return {
    schema: "gwyf.lobby-sighting.v1",
    protocolVersion: 1,
    sightingId: randomUUID(),
    observedAt,
    lobby: {
      steamLobbyId: "10977524123456789",
      name: "build-smoke lobby",
      members: 4,
      capacity: 12,
      map: "Museum",
      mode: "Classic",
    },
    observer: {
      installationId: client.observerId,
      clientVersion: "0.3.0",
      searchScope: "default",
    },
  };
}

async function ingestSighting(server: BootedServer, client: ColdClient): Promise<void> {
  const body = JSON.stringify(makeSighting(client, new Date().toISOString()));
  const response = await fetch(`${server.base}/v1/ingest/lobby-sightings`, {
    method: "POST",
    headers: {
      "content-type": "application/json",
      "x-observer-id": client.observerId,
      "x-observer-signature": signBytes(client.keyPair, body),
    },
    body,
  });
  if (![200, 202].includes(response.status)) {
    fail(`ingest -> ${response.status}: ${await response.text()}`, server.child);
  }
}

async function assertLiveLobby(server: BootedServer, label: string): Promise<void> {
  const live = await getJson<{ count: number; activeObservers: number; lobbies: Array<{ name: string; status: string }> }>(
    server,
    "/v1/lobbies/live",
  );
  const lobby = live.lobbies.find((l) => l.name === "build-smoke lobby");
  if (!lobby) fail(`${label}: lobby not visible in /v1/lobbies/live`, server.child);
  if (lobby.status !== "active") fail(`${label}: expected status active, got ${lobby.status}`, server.child);
  if (live.activeObservers < 1) fail(`${label}: activeObservers expected >= 1`, server.child);
  console.log(`[build-smoke] ${label}: lobby visible (status=${lobby.status}, activeObservers=${live.activeObservers})`);
}

async function main(): Promise<void> {
  if (!existsSync(SERVER_ENTRY)) {
    fail(`${SERVER_ENTRY} not found — run "pnpm build" first`);
  }

  const workDir = await mkdtemp(join(tmpdir(), "gwyf-build-smoke-"));
  const dbPath = join(workDir, "gwyf.db");
  let bootNo = 0;
  let client: ColdClient | undefined;

  try {
    let server = await bootServer(dbPath);
    bootNo += 1;
    console.log(`[build-smoke] cold boot #${bootNo}: plain "node apps/api/dist/server.js" (port ${server.port})`);
    await assertConfigContract(server, `cold boot #${bootNo} config`);
    client = await registerObserver(server);
    await ingestSighting(server, client);
    await assertLiveLobby(server, `cold boot #${bootNo}`);
    await stopServer(server);

    server = await bootServer(dbPath);
    bootNo += 1;
    console.log(`[build-smoke] cold restart #${bootNo}: same database, fresh process`);
    await assertConfigContract(server, `cold restart #${bootNo} config`);
    if (!client) fail("cold restart: no client to ingest with", server.child);
    await ingestSighting(server, client);
    await assertLiveLobby(server, `cold restart #${bootNo}`);
    await stopServer(server);

    console.log(
      "[build-smoke] PASS: compiled server boots under plain Node; config contract + ingest survive cold boot and restart.",
    );
    await rm(workDir, { recursive: true, force: true });
    process.exit(0);
  } finally {
    await rm(workDir, { recursive: true, force: true }).catch(() => undefined);
  }
}

main().catch((err) => {
  console.error(`[build-smoke] FAIL: ${(err as Error).message}`);
  process.exitCode = 1;
});