using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ARC.Replay
{
    // Minimal JSON model used only for canonicalisation. Pure C#: no UnityEngine, so the same
    // files compile in a plain .NET harness and can be exercised without the Editor.
    //
    // NUMBERS ARE NOT DOUBLES. A number is kept as the decimal value of its source text, so
    // "0.0000015" is exactly 1.5e-6 and rounds the same way in C# and in Python (Decimal).
    // Going through double would make the 6-dp rounding depend on binary representation,
    // which is the one place Unity and Python could silently disagree.

    public abstract class JNode { }

    public sealed class JObject : JNode
    {
        // Sorted ordinally on insert. Canonical output needs sorted keys, and the delta code
        // works on the same tree, so sorting once here keeps both paths identical.
        public readonly SortedDictionary<string, JNode> Members =
            new SortedDictionary<string, JNode>(StringComparer.Ordinal);
    }

    public sealed class JArray : JNode
    {
        public readonly List<JNode> Items = new List<JNode>();
    }

    public sealed class JString : JNode
    {
        public readonly string Value;
        public JString(string value) { Value = value; }
    }

    public sealed class JNumber : JNode
    {
        public readonly decimal Value;
        public JNumber(decimal value) { Value = value; }
    }

    public sealed class JBool : JNode
    {
        public readonly bool Value;
        public JBool(bool value) { Value = value; }
    }

    public sealed class JNull : JNode
    {
        public static readonly JNull Instance = new JNull();
        JNull() { }
    }

    public static class JsonCanon
    {
        // Parses strict JSON. Rejects duplicate keys, NaN/Infinity, trailing content and
        // numbers that do not fit decimal. JsonUtility never emits any of those, so hitting one
        // means the state cannot be canonicalised and the caller must not write a hash for it.
        public static JNode Parse(string text)
        {
            if (text == null) throw new FormatException("JSON text is null");
            var p = new Parser(text);
            p.SkipWhitespace();
            JNode root = p.ParseValue();
            p.SkipWhitespace();
            if (p.Pos != text.Length) throw p.Error("trailing content");
            return root;
        }

        // Canonical text for any JSON document: compact, sorted keys, canonical numbers.
        public static string Canonicalize(string text)
        {
            return ToCanonicalString(Parse(text));
        }

        public static string ToCanonicalString(JNode node)
        {
            var sb = new StringBuilder();
            Write(node, sb);
            return sb.ToString();
        }

        // Canonical number text. Rule, identical in Python (save_game_logs.canon_format_number):
        //   1. round the exact decimal to 6 dp, ties to even
        //   2. zero (including negative zero) is "0"
        //   3. an integral value is written as an integer
        //   4. otherwise exactly six decimals, e.g. 0.500000
        public static string FormatNumber(decimal value)
        {
            decimal q = Math.Round(value, 6, MidpointRounding.ToEven);
            if (q == 0m) return "0";
            decimal whole = decimal.Truncate(q);
            if (q == whole) return whole.ToString("0", CultureInfo.InvariantCulture);
            return q.ToString("F6", CultureInfo.InvariantCulture);
        }

        public static string Sha256Hex(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        static void Write(JNode node, StringBuilder sb)
        {
            if (node is JObject obj)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in obj.Members)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(kv.Key, sb);
                    sb.Append(':');
                    Write(kv.Value, sb);
                }
                sb.Append('}');
            }
            else if (node is JArray arr)
            {
                sb.Append('[');
                for (int i = 0; i < arr.Items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Write(arr.Items[i], sb);
                }
                sb.Append(']');
            }
            else if (node is JString str)
            {
                WriteString(str.Value, sb);
            }
            else if (node is JNumber num)
            {
                sb.Append(FormatNumber(num.Value));
            }
            else if (node is JBool b)
            {
                sb.Append(b.Value ? "true" : "false");
            }
            else if (node is JNull)
            {
                sb.Append("null");
            }
            else
            {
                throw new InvalidOperationException("unknown JSON node " + node.GetType().Name);
            }
        }

        // Escapes: quote, backslash, the five short control escapes, and \u00xx (lowercase)
        // for every other control character. Everything else is written raw as UTF-8.
        static void WriteString(string s, StringBuilder sb)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string s;
            public int Pos;

            public Parser(string text) { s = text; }

            public FormatException Error(string message)
            {
                return new FormatException("JSON " + message + " at offset " + Pos);
            }

            public void SkipWhitespace()
            {
                while (Pos < s.Length)
                {
                    char c = s[Pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') Pos++;
                    else break;
                }
            }

            public JNode ParseValue()
            {
                if (Pos >= s.Length) throw Error("unexpected end of input");
                char c = s[Pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return new JString(ParseString());
                    case 't': Expect("true"); return new JBool(true);
                    case 'f': Expect("false"); return new JBool(false);
                    case 'n': Expect("null"); return JNull.Instance;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error("unexpected character '" + c + "'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, Pos, word, 0, word.Length) != 0) throw Error("invalid literal");
                Pos += word.Length;
            }

            JObject ParseObject()
            {
                var obj = new JObject();
                Pos++; // {
                SkipWhitespace();
                if (Pos < s.Length && s[Pos] == '}') { Pos++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    if (Pos >= s.Length || s[Pos] != '"') throw Error("expected object key");
                    string key = ParseString();
                    SkipWhitespace();
                    if (Pos >= s.Length || s[Pos] != ':') throw Error("expected ':'");
                    Pos++;
                    SkipWhitespace();
                    JNode value = ParseValue();
                    if (obj.Members.ContainsKey(key)) throw Error("duplicate key '" + key + "'");
                    obj.Members.Add(key, value);
                    SkipWhitespace();
                    if (Pos >= s.Length) throw Error("unterminated object");
                    if (s[Pos] == ',') { Pos++; continue; }
                    if (s[Pos] == '}') { Pos++; return obj; }
                    throw Error("expected ',' or '}'");
                }
            }

            JArray ParseArray()
            {
                var arr = new JArray();
                Pos++; // [
                SkipWhitespace();
                if (Pos < s.Length && s[Pos] == ']') { Pos++; return arr; }
                while (true)
                {
                    SkipWhitespace();
                    arr.Items.Add(ParseValue());
                    SkipWhitespace();
                    if (Pos >= s.Length) throw Error("unterminated array");
                    if (s[Pos] == ',') { Pos++; continue; }
                    if (s[Pos] == ']') { Pos++; return arr; }
                    throw Error("expected ',' or ']'");
                }
            }

            string ParseString()
            {
                Pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (Pos >= s.Length) throw Error("unterminated string");
                    char c = s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Error("raw control character in string");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (Pos >= s.Length) throw Error("unterminated escape");
                    char e = s[Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (Pos + 4 > s.Length) throw Error("short \\u escape");
                            int code;
                            if (!int.TryParse(s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw Error("bad \\u escape");
                            sb.Append((char)code);
                            Pos += 4;
                            break;
                        default:
                            throw Error("bad escape '\\" + e + "'");
                    }
                }
            }

            JNumber ParseNumber()
            {
                int start = Pos;
                if (s[Pos] == '-') Pos++;
                int digits = ScanDigits();
                if (digits == 0) throw Error("bad number");
                if (Pos < s.Length && s[Pos] == '.')
                {
                    Pos++;
                    if (ScanDigits() == 0) throw Error("bad fraction");
                }
                if (Pos < s.Length && (s[Pos] == 'e' || s[Pos] == 'E'))
                {
                    Pos++;
                    if (Pos < s.Length && (s[Pos] == '+' || s[Pos] == '-')) Pos++;
                    if (ScanDigits() == 0) throw Error("bad exponent");
                }
                string text = s.Substring(start, Pos - start);
                decimal value;
                if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw Error("number out of decimal range '" + text + "'");
                return new JNumber(value);
            }

            int ScanDigits()
            {
                int n = 0;
                while (Pos < s.Length && s[Pos] >= '0' && s[Pos] <= '9') { Pos++; n++; }
                return n;
            }
        }
    }
}
