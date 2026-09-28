using System.Globalization;
using System.Text.RegularExpressions;
using WebMacros.Engine.Syntax;

namespace WebMacros.Engine.Scripting;

/// <summary>Turns resolved TAG/EVENT/CLICK parameters into <see cref="ElementSpec"/> objects.</summary>
public static partial class TagSpecBuilder
{
    public static readonly IReadOnlySet<string> KnownExtractTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TXT", "TXTALL", "HTM", "HREF", "TITLE", "ALT", "CHECKED" };

    [GeneratedRegex(@"^\s*(?<rel>[Rr])?\s*(?<num>[+-]?\d+)\s*$")]
    private static partial Regex PosRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_:.-]*$")]
    private static partial Regex AttrKeyRegex();

    public static bool TryParsePos(string pos, out int value, out bool relative)
    {
        var m = PosRegex().Match(pos);
        value = 0; relative = false;
        if (!m.Success) return false;
        relative = m.Groups["rel"].Success;
        value = int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture);
        if (value == 0) return false;
        if (!relative && value < 0) return false;
        return true;
    }

    public static TypeSpec ParseType(string type)
    {
        var t = type.Trim().ToUpperInvariant();
        if (t.Length == 0) throw new MacroSyntaxException("TYPE= must not be empty");
        if (t is "*" or "ANY") return new TypeSpec("*", null);
        var idx = t.IndexOf(':');
        if (idx < 0) return new TypeSpec(t, null);
        var tag = t[..idx];
        var sub = t[(idx + 1)..];
        return new TypeSpec(tag.Length == 0 ? "*" : tag, sub.Length == 0 ? null : sub);
    }

    /// <summary>Parses <c>KEY:value&amp;&amp;KEY2:value2</c>. <c>*</c> (or empty) means "any element".</summary>
    public static IReadOnlyList<AttrCondition> ParseAttr(string? attr)
    {
        if (string.IsNullOrWhiteSpace(attr) || attr.Trim() == "*") return Array.Empty<AttrCondition>();
        var list = new List<AttrCondition>();
        foreach (var part in attr.Split("&&"))
        {
            var idx = part.IndexOf(':');
            if (idx <= 0) throw new MacroSyntaxException($"ATTR part \"{part}\" must look like KEY:value (e.g. NAME:q, TXT:Next*)");
            var key = part[..idx].Trim();
            if (!AttrKeyRegex().IsMatch(key)) throw new MacroSyntaxException($"Invalid ATTR key \"{key}\"");
            list.Add(new AttrCondition(key.ToUpperInvariant(), part[(idx + 1)..]));
        }
        return list;
    }

    /// <summary>Builds the spec for a TAG command whose parameters are already variable-resolved.</summary>
    public static ElementSpec BuildTag(string? pos, string? type, string? attr, string? form, string? xpath, string? selector,
        string? content, string? extract, FrameSpec? frame)
    {
        var p = 1; var relative = false;
        if (pos is not null && !TryParsePos(pos, out p, out relative))
            throw new MacroSyntaxException($"Invalid POS=\"{pos}\" (expected n or Rn)");
        if (xpath is null && selector is null && type is null)
            throw new MacroSyntaxException("TAG requires TYPE=, XPATH= or SELECTOR=");
        if (relative && (xpath is not null || selector is not null))
            throw new MacroSyntaxException("Relative POS=R cannot be used with XPATH/SELECTOR");

        var pressEnter = false;
        if (content is not null && content.EndsWith("<ENTER>", StringComparison.OrdinalIgnoreCase))
        {
            content = content[..^7];
            pressEnter = true;
        }

        string? ex = null;
        if (extract is not null)
        {
            ex = extract.Trim().ToUpperInvariant();
            if (!KnownExtractTypes.Contains(ex) && !AttrKeyRegex().IsMatch(ex))
                throw new MacroSyntaxException($"Invalid EXTRACT=\"{extract}\"");
        }

        return new ElementSpec
        {
            Kind = "tag",
            Pos = p,
            Relative = relative,
            Type = xpath is null && selector is null ? ParseType(type!) : null,
            Attrs = xpath is null && selector is null ? ParseAttr(attr) : null,
            Form = string.IsNullOrWhiteSpace(form) ? null : ParseAttr(form),
            XPath = xpath,
            Selector = selector,
            Content = content,
            PressEnter = pressEnter,
            Extract = ex,
            Frame = frame is { IsTop: true } ? null : frame,
        };
    }

    public static ElementSpec BuildEvent(string type, string? selector, string? xpath, string? key, string? chars, FrameSpec? frame)
    {
        int? k = null;
        if (key is not null)
        {
            if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kv))
                throw new MacroSyntaxException($"EVENT: KEY= must be a key code number, got \"{key}\"");
            k = kv;
        }
        return new ElementSpec
        {
            Kind = "event",
            EventType = type.ToUpperInvariant(),
            Selector = selector,
            XPath = xpath,
            Key = k,
            Chars = chars,
            Frame = frame is { IsTop: true } ? null : frame,
        };
    }

    public static ElementSpec BuildPoint(double x, double y, string? content, FrameSpec? frame) => new()
    {
        Kind = "point", X = x, Y = y, Content = content, Frame = frame is { IsTop: true } ? null : frame,
    };

    public static ElementSpec BuildFrameCheck(FrameSpec frame) => new() { Kind = "frame", Frame = frame };
}
