using System.Text;

namespace WebMacros.Engine.Recording;

/// <summary>Accumulates recorded actions and turns them into macro lines.</summary>
public sealed class MacroRecorder
{
    private readonly List<string> _lines = new();

    /// <summary>ATTR keys in order of preference when several identify the element.</summary>
    public static readonly IReadOnlyList<string> AttrPreference = new[] { "ID", "NAME", "TXT", "HREF", "ALT", "PLACEHOLDER", "CLASS", "SRC", "*" };

    public IReadOnlyList<string> Lines => _lines;
    public event Action<string>? LineAdded;

    public void Start(string? currentUrl, string version = "1.0.0")
    {
        _lines.Clear();
        Append("VERSION BUILD=" + version.Replace(".", ""));
        Append("' Recorded by WebMacros on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        Append("TAB T=1");
        if (!string.IsNullOrEmpty(currentUrl) && currentUrl != "about:blank") Append("URL GOTO=" + currentUrl);
    }

    public string GetMacroText() => string.Join(Environment.NewLine, _lines) + Environment.NewLine;

    private void Append(string line)
    {
        _lines.Add(line);
        LineAdded?.Invoke(line);
    }

    public void AddNavigation(string url)
    {
        var line = "URL GOTO=" + url;
        if (_lines.Count > 0 && _lines[^1] == line) return;
        Append(line);
    }

    public void AddBack() => Append("BACK");
    public void AddRefresh() => Append("REFRESH");
    public void AddTabOpen() => Append("TAB OPEN");
    public void AddTabSwitch(int index1Based) => Append("TAB T=" + index1Based);
    public void AddTabClose() => Append("TAB CLOSE");

    /// <summary>Converts a recorded action into a TAG line and appends it. Returns the line or null if ignored.</summary>
    public string? Add(RecordedAction action)
    {
        var line = ToLine(action);
        if (line is null) return null;

        if (action.Action is "type" or "select" && _lines.Count > 0)
        {
            // Typing into the same field again replaces the previous line.
            var prefix = line[..line.IndexOf(" CONTENT=", StringComparison.Ordinal)];
            if (_lines[^1].StartsWith(prefix + " CONTENT=", StringComparison.Ordinal)) _lines.RemoveAt(_lines.Count - 1);
        }
        if (action.Action == "submit" && _lines.Count > 0)
        {
            var prefix = line[..line.IndexOf(" CONTENT=", StringComparison.Ordinal)];
            if (_lines[^1].StartsWith(prefix + " CONTENT=", StringComparison.Ordinal)) _lines.RemoveAt(_lines.Count - 1);
        }
        if (action.Password) Append("' Note: the next line contains a password in plain text");
        Append(line);
        return line;
    }

    public static AttrCandidate ChooseCandidate(IReadOnlyList<AttrCandidate> candidates)
    {
        foreach (var key in AttrPreference)
        {
            var c = candidates.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && x.Pos > 0);
            if (c is not null) return c;
        }
        return new AttrCandidate("*", "*", 1);
    }

    /// <summary>Escapes a value for use inside ATTR= or CONTENT= without quotes.</summary>
    public static string EscapeValue(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                ' ' => "<SP>",
                '\n' => "<BR>",
                '\r' => "",
                '\t' => "<TAB>",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    public static string? ToLine(RecordedAction a)
    {
        if (string.IsNullOrEmpty(a.Type)) return null;
        var c = ChooseCandidate(a.Candidates);
        var attr = c.Key == "*" ? "*" : c.Key + ":" + EscapeValue(c.Value);
        var head = $"TAG POS={c.Pos} TYPE={a.Type} ATTR={attr}";
        switch (a.Action)
        {
            case "click":
                return head;
            case "type":
                var v = a.Value ?? "";
                return head + " CONTENT=" + EscapeValue(v);
            case "select":
                var values = a.Values ?? Array.Empty<string>();
                if (values.Count == 0) return null;
                return head + " CONTENT=" + string.Join(":", values.Select(x => "%" + EscapeValue(x)));
            case "check":
                return head + " CONTENT=" + (a.Checked == true ? "YES" : "NO");
            case "submit":
                return head + " CONTENT=" + EscapeValue(a.Value ?? "") + "<ENTER>";
            default:
                return null;
        }
    }
}
