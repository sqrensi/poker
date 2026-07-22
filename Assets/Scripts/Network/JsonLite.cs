using System;
using System.Collections.Generic;
using System.Globalization;

namespace Poker.Network
{
    /// <summary>Минимальный JSON-парсер без внешних зависимостей (Unity).</summary>
    public sealed class JsonLite
    {
        public enum Kind { Null, Bool, Number, String, Array, Object }

        public Kind ValueKind = Kind.Null;
        public string StringValue;
        public double NumberValue;
        public bool BoolValue;
        public List<JsonLite> Array;
        public Dictionary<string, JsonLite> Object;

        public static bool TryParse(string json, out JsonLite root)
        {
            root = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                int i = 0;
                SkipWs(json, ref i);
                root = ParseValue(json, ref i);
                SkipWs(json, ref i);
                return i >= json.Length;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetProperty(string name, out JsonLite value)
        {
            value = null;
            return ValueKind == Kind.Object &&
                   Object != null &&
                   Object.TryGetValue(name, out value);
        }

        public string GetString(string name, string def = "")
        {
            if (!TryGetProperty(name, out var v) || v.ValueKind != Kind.String)
                return def;
            return v.StringValue ?? def;
        }

        public int GetInt(string name, int def = 0)
        {
            if (!TryGetProperty(name, out var v) || v.ValueKind != Kind.Number)
                return def;
            return (int)v.NumberValue;
        }

        public bool GetBool(string name, bool def = false)
        {
            if (!TryGetProperty(name, out var v) || v.ValueKind != Kind.Bool)
                return def;
            return v.BoolValue;
        }

        public IEnumerable<JsonLite> EnumerateArray()
        {
            if (ValueKind != Kind.Array || Array == null) yield break;
            for (int i = 0; i < Array.Count; i++)
                yield return Array[i];
        }

        public string AsString()
        {
            return ValueKind == Kind.String ? StringValue ?? "" : "";
        }

        public bool TryGetInt32(out int value)
        {
            if (ValueKind != Kind.Number)
            {
                value = 0;
                return false;
            }
            value = (int)NumberValue;
            return true;
        }

        static void SkipWs(string json, ref int i)
        {
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        }

        static JsonLite ParseValue(string json, ref int i)
        {
            SkipWs(json, ref i);
            if (i >= json.Length) throw new FormatException("Unexpected end");

            char c = json[i];
            if (c == '{') return ParseObject(json, ref i);
            if (c == '[') return ParseArray(json, ref i);
            if (c == '"') return ParseString(json, ref i);
            if (c == 't' || c == 'f') return ParseBool(json, ref i);
            if (c == 'n') return ParseNull(json, ref i);
            if (c == '-' || char.IsDigit(c)) return ParseNumber(json, ref i);
            throw new FormatException("Unexpected char: " + c);
        }

        static JsonLite ParseObject(string json, ref int i)
        {
            i++; // {
            var obj = new JsonLite { ValueKind = Kind.Object, Object = new Dictionary<string, JsonLite>() };
            SkipWs(json, ref i);
            if (i < json.Length && json[i] == '}')
            {
                i++;
                return obj;
            }

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                var key = ParseString(json, ref i).StringValue;
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') throw new FormatException("Expected ':'");
                i++;
                obj.Object[key] = ParseValue(json, ref i);
                SkipWs(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == '}')
                {
                    i++;
                    break;
                }
                if (json[i] != ',') throw new FormatException("Expected ',' or '}'");
                i++;
            }
            return obj;
        }

        static JsonLite ParseArray(string json, ref int i)
        {
            i++; // [
            var arr = new JsonLite { ValueKind = Kind.Array, Array = new List<JsonLite>() };
            SkipWs(json, ref i);
            if (i < json.Length && json[i] == ']')
            {
                i++;
                return arr;
            }

            while (i < json.Length)
            {
                arr.Array.Add(ParseValue(json, ref i));
                SkipWs(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == ']')
                {
                    i++;
                    break;
                }
                if (json[i] != ',') throw new FormatException("Expected ',' or ']'");
                i++;
            }
            return arr;
        }

        static JsonLite ParseString(string json, ref int i)
        {
            i++; // "
            var sb = new System.Text.StringBuilder();
            while (i < json.Length)
            {
                char c = json[i++];
                if (c == '"')
                    return new JsonLite { ValueKind = Kind.String, StringValue = sb.ToString() };
                if (c == '\\')
                {
                    if (i >= json.Length) throw new FormatException("Bad escape");
                    char e = json[i++];
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
                            if (i + 3 >= json.Length) throw new FormatException("Bad unicode");
                            sb.Append((char)int.Parse(json.Substring(i, 4), NumberStyles.HexNumber));
                            i += 4;
                            break;
                        default: throw new FormatException("Bad escape: " + e);
                    }
                }
                else sb.Append(c);
            }
            throw new FormatException("Unterminated string");
        }

        static bool MatchAt(string json, int i, string token)
        {
            return i + token.Length <= json.Length &&
                   string.Compare(json, i, token, 0, token.Length, StringComparison.Ordinal) == 0;
        }

        static JsonLite ParseBool(string json, ref int i)
        {
            if (MatchAt(json, i, "true"))
            {
                i += 4;
                return new JsonLite { ValueKind = Kind.Bool, BoolValue = true };
            }
            if (MatchAt(json, i, "false"))
            {
                i += 5;
                return new JsonLite { ValueKind = Kind.Bool, BoolValue = false };
            }
            throw new FormatException("Bad bool");
        }

        static JsonLite ParseNull(string json, ref int i)
        {
            if (!MatchAt(json, i, "null")) throw new FormatException("Bad null");
            i += 4;
            return new JsonLite { ValueKind = Kind.Null };
        }

        static JsonLite ParseNumber(string json, ref int i)
        {
            int start = i;
            if (json[i] == '-') i++;
            while (i < json.Length && char.IsDigit(json[i])) i++;
            if (i < json.Length && json[i] == '.')
            {
                i++;
                while (i < json.Length && char.IsDigit(json[i])) i++;
            }
            if (i < json.Length && (json[i] == 'e' || json[i] == 'E'))
            {
                i++;
                if (i < json.Length && (json[i] == '+' || json[i] == '-')) i++;
                while (i < json.Length && char.IsDigit(json[i])) i++;
            }
            string num = json.Substring(start, i - start);
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw new FormatException("Bad number");
            return new JsonLite { ValueKind = Kind.Number, NumberValue = d };
        }
    }
}
