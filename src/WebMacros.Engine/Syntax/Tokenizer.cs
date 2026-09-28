using System.Text;
using System.Text.RegularExpressions;

namespace WebMacros.Engine.Syntax;

/// <summary>
/// Splits an iMacros-style line into a command word and parameter tokens.
/// Whitespace separates tokens except inside double quotes. Inside quotes, <c>\"</c> and <c>\\</c>
/// are escapes (plus <c>\n</c>, <c>\t</c>). Quotes may start in the middle of a token
/// (e.g. <c>XPATH="//a[@id='x']"</c> or <c>EVAL("1+1")</c>).
/// </summary>
public static partial class Tokenizer
{
    [GeneratedRegex(@"^(?<key>[A-Za-z_][A-Za-z0-9_]*)=(?<value>.*)$", RegexOptions.Singleline)]
    private static partial Regex KeyValueRegex();

    public static (string Command, IReadOnlyList<Token> Tokens) TokenizeLine(string line)
    {
        var raw = SplitRaw(line);
        if (raw.Count == 0) return ("", Array.Empty<Token>());
        var command = raw[0].ToUpperInvariant();
        var tokens = new List<Token>(raw.Count - 1);
        for (var i = 1; i < raw.Count; i++) tokens.Add(ToToken(raw[i]));
        return (command, tokens);
    }

    /// <summary>Splits on unquoted whitespace, keeping quotes in the raw pieces.</summary>
    public static List<string> SplitRaw(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < line.Length)
                {
                    sb.Append(line[++i]);
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    hasToken = false;
                }
                continue;
            }
            if (c == '"') inQuotes = true;
            sb.Append(c);
            hasToken = true;
        }
        if (inQuotes) throw new MacroSyntaxException("Unterminated quoted string");
        if (hasToken) result.Add(sb.ToString());
        return result;
    }

    public static Token ToToken(string raw)
    {
        var m = KeyValueRegex().Match(raw);
        if (m.Success)
        {
            var v = m.Groups["value"].Value;
            var (value, quoted) = Unquote(v);
            return new Token(raw, m.Groups["key"].Value.ToUpperInvariant(), value, quoted);
        }
        var (pv, pq) = Unquote(raw);
        return new Token(raw, null, pv, pq);
    }

    /// <summary>If <paramref name="s"/> is entirely one quoted string, returns its unescaped content.</summary>
    public static (string Value, bool Quoted) Unquote(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"' && IsSingleQuotedString(s))
        {
            var sb = new StringBuilder(s.Length);
            for (var i = 1; i < s.Length - 1; i++)
            {
                var c = s[i];
                if (c == '\\' && i + 1 < s.Length - 1)
                {
                    var n = s[++i];
                    sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => n });
                }
                else sb.Append(c);
            }
            return (sb.ToString(), true);
        }
        return (s, false);
    }

    private static bool IsSingleQuotedString(string s)
    {
        // The closing quote must be the last character: scan for the first unescaped quote after index 0.
        for (var i = 1; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '"') return i == s.Length - 1;
        }
        return false;
    }

    /// <summary>Quotes a value for writing back into a macro if needed.</summary>
    public static string QuoteIfNeeded(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"')) return value;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    }
}
