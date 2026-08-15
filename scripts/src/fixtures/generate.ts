/**
 * One-shot generator for the cross-language signing fixtures:
 *
 *   node scripts/src/fixtures/generate.ts
 *
 * Outputs (committed, read by mod/test):
 *   scripts/fixtures/signing/test-keypair.json         TEST-ONLY key material
 *   scripts/fixtures/canonical-lobby-sighting.json     golden sighting payload
 *   scripts/fixtures/canonical-lobby-sighting.txt      canonical bytes (utf8)
 *   scripts/fixtures/canonical-lobby-sighting.sig.txt  TS signature over canonical
 *   scripts/fixtures/test-observer-id.txt              deriveObserverId(pub)
 *
 * The signature is deterministic (Ed25519), so a C# test can assert an exact
 * byte match of its own signature over the same canonical bytes.
 */
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import {
  deriveObserverId,
  generateObserverKeyPair,
  signBytes,
  type ObserverKeyPair,
} from "@gwyf/protocol";
import { canonicalize, type CanonicalValue } from "./canonicalize.js";

const fixturesDir = join(
  dirname(fileURLToPath(import.meta.url)),
  "..",
  "..",
  "fixtures",
);

const keyPair: ObserverKeyPair = generateObserverKeyPair();
const observerId = deriveObserverId(keyPair.publicKeyHex);

const payload: CanonicalValue = {
  schema: "gwyf.lobby-sighting.v1",
  protocolVersion: 1,
  sightingId: "2f5a2a4d-0a34-4dbb-9d8f-7c29a3f0c71e",
  observedAt: "2026-08-15T12:00:00.0000000+00:00",
  lobby: {
    steamLobbyId: "6052dc1a-2f0e-4d84-b5e3-1d5cd616d419",
    name: "TestLobby",
    members: 3,
    capacity: 12,
    map: "Castle",
    mode: "Classic",
    rawMetadata: {
      lobbyType: "Public",
      isVisible: "True",
      identifier: "6052dc1a-2f0e-4d84-b5e3-1d5cd616d419",
      observationBatchId: "batch_abc123def456",
      searchId: "search_1234abcd5678",
    },
  },
  observer: {
    installationId: observerId,
    clientVersion: "0.2.0",
    gameBuild: "256.999952",
    searchScope: "default",
  },
};

const canonical = canonicalize(payload);
const signature = signBytes(keyPair, canonical);

mkdirSync(fixturesDir, { recursive: true });
mkdirSync(join(fixturesDir, "signing"), { recursive: true });

writeFileSync(
  join(fixturesDir, "signing", "test-keypair.json"),
  JSON.stringify({ ...keyPair, note: "TEST-ONLY fixture key - never production" }, null, 2) + "\n",
);
writeFileSync(
  join(fixturesDir, "canonical-lobby-sighting.json"),
  JSON.stringify(payload, null, 2) + "\n",
);
writeFileSync(join(fixturesDir, "canonical-lobby-sighting.txt"), canonical);
writeFileSync(
  join(fixturesDir, "canonical-lobby-sighting.sig.txt"),
  signature + "\n",
);
writeFileSync(join(fixturesDir, "test-observer-id.txt"), observerId + "\n");

console.log(`fixtures written to ${fixturesDir}`);
console.log(`canonical (${canonical.length} bytes): ${canonical}`);
console.log(`signature: ${signature}`);
console.log(`observer:  ${observerId}`);