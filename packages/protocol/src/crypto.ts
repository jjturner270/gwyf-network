import {
  createHash,
  createPrivateKey,
  createPublicKey,
  generateKeyPairSync,
  sign as nodeSign,
  verify as nodeVerify,
} from "node:crypto";

export interface ObserverKeyPair {
  publicKeyHex: string;
  privateKeyHex: string;
}

export function generateObserverKeyPair(): ObserverKeyPair {
  const { publicKey, privateKey } = generateKeyPairSync("ed25519");
  const pubJwk = publicKey.export({ format: "jwk" });
  const privJwk = privateKey.export({ format: "jwk" });
  return {
    publicKeyHex: Buffer.from(pubJwk.x ?? "", "base64url").toString("hex"),
    privateKeyHex: Buffer.from(privJwk.d ?? "", "base64url").toString("hex"),
  };
}

function publicKeyObjectFromHex(publicKeyHex: string) {
  return createPublicKey({
    key: {
      kty: "OKP",
      crv: "Ed25519",
      x: Buffer.from(publicKeyHex, "hex").toString("base64url"),
    },
    format: "jwk",
  });
}

export function signBytes(keyPair: ObserverKeyPair, data: Uint8Array | string): string {
  const bytes = typeof data === "string" ? Buffer.from(data, "utf8") : data;
  const key = createPrivateKey({
    key: {
      kty: "OKP",
      crv: "Ed25519",
      d: Buffer.from(keyPair.privateKeyHex, "hex").toString("base64url"),
      x: Buffer.from(keyPair.publicKeyHex, "hex").toString("base64url"),
    },
    format: "jwk",
  });
  return nodeSign(null, bytes, key).toString("base64");
}

export function verifySignature(
  publicKeyHex: string,
  data: Uint8Array | string,
  signatureBase64: string,
): boolean {
  const bytes = typeof data === "string" ? Buffer.from(data, "utf8") : data;
  try {
    const key = publicKeyObjectFromHex(publicKeyHex);
    return nodeVerify(null, bytes, key, Buffer.from(signatureBase64, "base64"));
  } catch {
    return false;
  }
}

export function deriveObserverId(publicKeyHex: string): string {
  const hash = createHash("sha256").update(publicKeyHex).digest("hex");
  return "obs_" + hash.slice(0, 32);
}