using System.Text;

namespace WebMacros.Engine.Runtime;

/// <summary>Minimal RFC 4180 CSV reader/writer (quoted fields, doubled quotes, embedded newlines).</summary>
public static class Csv
{
    public static List<List<string>> Parse(string text, char delimiter = ',')
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }
            if (c == '"' && !fieldStarted) { inQuotes = true; fieldStarted = true; continue; }
            if (c == delimiter) { row.Add(field.ToString()); field.Clear(); fieldStarted = false; continue; }
            if (c == '\r') continue;
            if (c == '\n')
            {
                row.Add(field.ToString()); field.Clear(); fieldStarted = false;
                rows.Add(row); row = new List<string>();
                continue;
            }
            field.Append(c);
            fieldStarted = true;
        }
        if (inQuotes) throw new FormatException("Unterminated quoted field in CSV");
        if (fieldStarted || field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        // Drop trailing completely empty lines.
        while (rows.Count > 0 && rows[^1].Count == 1 && rows[^1][0].Length == 0) rows.RemoveAt(rows.Count - 1);
        return rows;
    }

    /// <summary>Formats a row like iMacros SAVEAS TYPE=EXTRACT: every field quoted, quotes doubled.</summary>
    public static string FormatRow(IEnumerable<string> fields, char delimiter = ',') =>
        string.Join(delimiter, fields.Select(f => "\"" + f.Replace("\"", "\"\"") + "\""));
}
