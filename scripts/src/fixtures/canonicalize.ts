/**
 * Reference canonical JSON implementation (TypeScript side of the golden
 * fixtures). Must byte-match mod/Identity/CanonicalJson.cs exactly:
 *
 *  - object keys sorted ascending (ordinal / code-unit order)
 *  - strings: '"' → \", '\' → \\, '\n' → \n, '\r' → \r, '\t' → \t,
 *    other control chars (< 0x20) → \uXXXX with lowercase hex
 *  - numbers, booleans, null emitted directly
 *  - arrays preserve order
 *  - no whitespace
 *
 * The C# test suite asserts equality against the fixture outputs produced by
 * this module, and both sides sign/verify the same bytes with the same test
 * key; Ed25519 signatures are deterministic, so an exact signature match is
 * byte-for-byte proof of agreement.
 */

export type CanonicalValue =
  | null
  | boolean
  | number
  | string
  | CanonicalValue[]
  | { [key: string]: CanonicalValue };

function escapeString(s: string): string {
  let out = '"';
  for (const ch of s) {
    switch (ch) {
      case '"':
        out += '\\"';
        break;
      case "\\":
        out += "\\\\";
        break;
      case "\n":
        out += "\\n";
        break;
      case "\r":
        out += "\\r";
        break;
      case "\t":
        out += "\\t";
        break;
      default:
        if (ch < "\x20") {
          out += "\\u" + ch.charCodeAt(0).toString(16).padStart(4, "0");
        } else {
          out += ch;
        }
    }
  }
  return out + '"';
}

export function canonicalize(value: CanonicalValue): string {
  if (value === null) return "null";
  if (typeof value === "boolean") return value ? "true" : "false";
  if (typeof value === "number") return String(value);
  if (typeof value === "string") return escapeString(value);
  if (Array.isArray(value)) {
    const inner = value.map(canonicalize).join(",");
    return "[" + inner + "]";
  }
  const keys = Object.keys(value).sort();
  const entries = keys
    .map((k) => escapeString(k) + ":" + canonicalize(value[k]));
  return "{" + entries.join(",") + "}";
}