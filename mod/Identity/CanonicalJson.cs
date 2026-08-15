using System.Text;

namespace GwyfCommunity.Identity;

/// <summary>
/// Deterministic JSON for canonical payloads (fixtures, golden tests).
///
/// Rules (identical on the TypeScript side in scripts/fixtures/canonicalize.ts):
///  - object keys sorted ascending (ordinal; both sides use code-unit order for
///    ASCII keys)
///  - strings UTF-8 escaped minimally: '"' → \", '\' → \\, control chars → \uXXXX
///    (lowercase hex); everything else verbatim
///  - integers, booleans and null emitted directly
///  - arrays preserve order
///  - no whitespace, no trailing newline
/// </summary>
public static class CanonicalJson
{
    public static string Serialize(CanonicalValue value) => Write(value);

    public static CanonicalValue Object(params (string Key, CanonicalValue Value)[] entries) => new(entries);

    private static string Write(CanonicalValue value, StringBuilder? sb = null)
    {
        sb ??= new StringBuilder();
        switch (value.Kind)
        {
            case CanonicalKind.Null:
                sb.Append("null");
                break;
            case CanonicalKind.Bool:
                sb.Append(value.BoolValue ? "true" : "false");
                break;
            case CanonicalKind.Number:
                sb.Append(value.NumberValue);
                break;
            case CanonicalKind.String:
                WriteString(sb, value.StringValue);
                break;
            case CanonicalKind.Array:
                sb.Append('[');
                for (var i = 0; i < value.ArrayValue.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Write(value.ArrayValue[i], sb);
                }
                sb.Append(']');
                break;
            case CanonicalKind.Object:
                sb.Append('{');
                var entries = value.ObjectValue.OrderBy(e => e.Key, StringComparer.Ordinal).ToArray();
                for (var i = 0; i < entries.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteString(sb, entries[i].Key);
                    sb.Append(':');
                    Write(entries[i].Value, sb);
                }
                sb.Append('}');
                break;
        }
        return sb.ToString();
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                        sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else
                        sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
    }

    public static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}

public enum CanonicalKind
{
    Null,
    Bool,
    Number,
    String,
    Array,
    Object,
}

public sealed class CanonicalValue
{
    public CanonicalKind Kind { get; }
    public bool BoolValue { get; }
    public string NumberValue { get; }
    public string StringValue { get; }
    public IReadOnlyList<CanonicalValue> ArrayValue { get; }
    public IReadOnlyList<(string Key, CanonicalValue Value)> ObjectValue { get; }

    private CanonicalValue(CanonicalKind kind, bool b = false, string number = "", string str = "",
        IReadOnlyList<CanonicalValue>? array = null,
        IReadOnlyList<(string, CanonicalValue)>? obj = null)
    {
        Kind = kind;
        BoolValue = b;
        NumberValue = number;
        StringValue = str;
        ArrayValue = array ?? Array.Empty<CanonicalValue>();
        ObjectValue = obj ?? Array.Empty<(string, CanonicalValue)>();
    }

    public CanonicalValue((string Key, CanonicalValue Value)[] entries)
        : this(CanonicalKind.Object, obj: entries)
    {
    }

    public static CanonicalValue Null() => new(CanonicalKind.Null);
    public static CanonicalValue Bool(bool value) => new(CanonicalKind.Bool, b: value);
    public static CanonicalValue Number(long value) => new(CanonicalKind.Number, number: value.ToString());
    public static CanonicalValue Str(string value) => new(CanonicalKind.String, str: value);
    public static CanonicalValue Arr(IEnumerable<CanonicalValue> values) =>
        new(CanonicalKind.Array, array: values.ToList());

    public static implicit operator CanonicalValue(string value) => Str(value);
    public static implicit operator CanonicalValue(long value) => Number(value);
    public static implicit operator CanonicalValue(bool value) => Bool(value);
}