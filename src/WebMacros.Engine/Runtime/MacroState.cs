using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WebMacros.Engine.Scripting;

namespace WebMacros.Engine.Runtime;

/// <summary>Variables and other state of a running macro (shared across loops).</summary>
public sealed partial class MacroState
{
    public const string ExtractSeparator = "[EXTRACT]";

    private readonly Dictionary<string, string> _vars = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _extracts = new();
    private readonly Func<string> _currentUrl;
    private readonly IEngineClock _clock;
    private List<List<string>>? _dataRows;

    /// <summary>Built-in variables that are accepted by SET but have no effect in WebMacros.</summary>
    public static readonly IReadOnlySet<string> IgnoredBuiltIns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "!REPLAYSPEED", "!ENCRYPTION", "!FILESTOPWATCH", "!WAITPAGECOMPLETE", "!SINGLESTEP", "!FILE_PROFILER",
        "!POPUP_ALLOWED", "!USERAGENT", "!FILELOG", "!MARKOLDDATA", "!DOWNLOAD_PDF", "!STOPWATCH_HEADER", "!FOLDER_STOPWATCH",
    };

    public MacroState(InterpreterOptions options, IEngineClock clock, Func<string> currentUrl)
    {
        _clock = clock;
        _currentUrl = currentUrl;
        TimeoutStep = options.DefaultTimeoutStepSeconds;
        TimeoutPage = options.DefaultTimeoutPageSeconds;
        DataSourceFolder = options.DataSourceFolder;
        DownloadFolder = options.DownloadFolder;
        MacroFolder = options.MacroFolder;
        for (var i = 0; i <= 9; i++) _vars["!VAR" + i] = "";
    }

    public int Loop { get; set; } = 1;
    public bool IsFirstLoop { get; set; } = true;
    public double TimeoutStep { get; private set; }
    public double TimeoutPage { get; private set; }
    public bool ErrorIgnore { get; private set; }
    public bool ErrorLoop { get; private set; }
    public bool ExtractTestPopup { get; private set; } = true;
    public string? DataSourcePath { get; private set; }
    public int DataSourceLine { get; private set; } = 1;
    public int? DataSourceColumns { get; private set; }
    public char DataSourceDelimiter { get; private set; } = ',';
    public string DataSourceFolder { get; private set; }
    public string DownloadFolder { get; private set; }
    public string MacroFolder { get; }
    public FrameSpec? Frame { get; set; }
    public double StopwatchTime { get; set; }
    public IReadOnlyList<string> ExtractValues => _extracts;
    public string Extract => string.Join(ExtractSeparator, _extracts);

    /// <summary>Raised when SET !LOOP is used in the first loop so the interpreter can adjust the counter.</summary>
    public event Action<int>? LoopChanged;

    public void AddExtract(string value) => _extracts.Add(value);
    public void ClearExtract() => _extracts.Clear();

    [GeneratedRegex(@"\{\{([^{}]+)\}\}")]
    private static partial Regex VarRefRegex();

    [GeneratedRegex(@"^!COL(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ColRegex();

    [GeneratedRegex(@"<(SP|BR|TAB|LF|CR)>", RegexOptions.IgnoreCase)]
    private static partial Regex EscapeRegex();

    /// <summary>Substitutes {{variables}} and then &lt;SP&gt;, &lt;BR&gt;, &lt;TAB&gt; escapes.</summary>
    public string Resolve(string? text) => text is null ? "" : Unescape(Substitute(text));

    public string? ResolveOptional(string? text) => text is null ? null : Resolve(text);

    public string Substitute(string text) => VarRefRegex().Replace(text, m => Get(m.Groups[1].Value.Trim()));

    public static string Unescape(string text) => EscapeRegex().Replace(text, m => m.Groups[1].Value.ToUpperInvariant() switch
    {
        "SP" => " ",
        "BR" or "LF" => "\n",
        "CR" => "\r",
        "TAB" => "\t",
        _ => m.Value,
    });

    public bool IsDefined(string name) => _vars.ContainsKey(name);

    public string Get(string name)
    {
        var upper = name.ToUpperInvariant();
        if (upper.StartsWith("!NOW:", StringComparison.Ordinal)) return NowFormatter.Format(_clock.Now, name[5..]);
        var col = ColRegex().Match(upper);
        if (col.Success) return GetColumn(int.Parse(col.Groups[1].Value, CultureInfo.InvariantCulture));
        switch (upper)
        {
            case "!LOOP": return Loop.ToString(CultureInfo.InvariantCulture);
            case "!EXTRACT": return Extract;
            case "!URLCURRENT": return _currentUrl();
            case "!STOPWATCHTIME": return StopwatchTime.ToString("0.000", CultureInfo.InvariantCulture);
            case "!TIMEOUT_STEP": return Num(TimeoutStep);
            case "!TIMEOUT_PAGE": case "!TIMEOUT": return Num(TimeoutPage);
            case "!ERRORIGNORE": return ErrorIgnore ? "YES" : "NO";
            case "!ERRORLOOP": return ErrorLoop ? "YES" : "NO";
            case "!EXTRACT_TEST_POPUP": return ExtractTestPopup ? "YES" : "NO";
            case "!DATASOURCE": return DataSourcePath ?? "";
            case "!DATASOURCE_LINE": return DataSourceLine.ToString(CultureInfo.InvariantCulture);
            case "!DATASOURCE_COLUMNS": return (DataSourceColumns ?? CurrentRowOrNull()?.Count ?? 0).ToString(CultureInfo.InvariantCulture);
            case "!DATASOURCE_DELIMITER": return DataSourceDelimiter.ToString();
            case "!FOLDER_DATASOURCE": return DataSourceFolder;
            case "!FOLDER_DOWNLOAD": return DownloadFolder;
            case "!NOW": return NowFormatter.Format(_clock.Now, "yyyymmdd_hhnnss");
        }
        if (_vars.TryGetValue(name, out var v)) return v;
        throw new MacroRuntimeException($"Variable {{{{{name}}}}} is not defined (use SET {name} value first)");
    }

    private static string Num(double d) => d.ToString(CultureInfo.InvariantCulture);

    private static bool ParseYesNo(string name, string value) => value.Trim().ToUpperInvariant() switch
    {
        "YES" or "TRUE" or "ON" or "1" => true,
        "NO" or "FALSE" or "OFF" or "0" => false,
        _ => throw new MacroRuntimeException($"{name} must be YES or NO, got \"{value}\""),
    };

    private static double ParseSeconds(string name, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d < 0)
            throw new MacroRuntimeException($"{name} must be a non-negative number of seconds, got \"{value}\"");
        return d;
    }

    private static int ParsePositiveInt(string name, string value)
    {
        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) || i < 1)
            throw new MacroRuntimeException($"{name} must be a positive integer, got \"{value}\"");
        return i;
    }

    /// <summary>SET semantics for built-in and user variables. Returns a warning message or null.</summary>
    public string? Set(string name, string value)
    {
        var upper = name.ToUpperInvariant();
        if (!upper.StartsWith('!'))
        {
            _vars[name] = value;
            return null;
        }
        if (upper.StartsWith("!VAR", StringComparison.Ordinal) && upper.Length == 5 && char.IsDigit(upper[4]))
        {
            _vars[upper] = value;
            return null;
        }
        switch (upper)
        {
            case "!EXTRACT":
                _extracts.Clear();
                if (!value.Equals("NULL", StringComparison.OrdinalIgnoreCase)) _extracts.Add(value);
                return null;
            case "!LOOP":
                var loop = ParsePositiveInt(name, value);
                if (!IsFirstLoop) return "SET !LOOP is only applied in the first loop; ignored";
                Loop = loop;
                LoopChanged?.Invoke(loop);
                return null;
            case "!TIMEOUT_STEP": TimeoutStep = ParseSeconds(name, value); return null;
            case "!TIMEOUT_PAGE": case "!TIMEOUT": TimeoutPage = ParseSeconds(name, value); return null;
            case "!ERRORIGNORE": ErrorIgnore = ParseYesNo(name, value); return null;
            case "!ERRORLOOP": ErrorLoop = ParseYesNo(name, value); return null;
            case "!EXTRACT_TEST_POPUP": ExtractTestPopup = ParseYesNo(name, value); return null;
            case "!DATASOURCE": LoadDataSource(value); return null;
            case "!DATASOURCE_LINE": DataSourceLine = ParsePositiveInt(name, value); return null;
            case "!DATASOURCE_COLUMNS": DataSourceColumns = ParsePositiveInt(name, value); return null;
            case "!DATASOURCE_DELIMITER":
                var d = value == "\\t" || value.Equals("TAB", StringComparison.OrdinalIgnoreCase) ? "\t" : value;
                if (d.Length != 1) throw new MacroRuntimeException("!DATASOURCE_DELIMITER must be a single character");
                DataSourceDelimiter = d[0];
                if (DataSourcePath is not null) LoadDataSource(DataSourcePath);
                return null;
            case "!FOLDER_DATASOURCE": DataSourceFolder = value; return null;
            case "!FOLDER_DOWNLOAD": DownloadFolder = value; return null;
        }
        if (upper.StartsWith("!COL", StringComparison.Ordinal) || upper.StartsWith("!NOW", StringComparison.Ordinal) ||
            upper is "!URLCURRENT" or "!STOPWATCHTIME")
            throw new MacroRuntimeException($"{name} is read-only");
        if (IgnoredBuiltIns.Contains(upper))
        {
            _vars[upper] = value;
            return $"{name} is accepted but has no effect in WebMacros";
        }
        throw new MacroRuntimeException($"Unknown built-in variable {name}");
    }

    public string ResolveDataSourcePath(string file)
    {
        if (Path.IsPathRooted(file)) return file;
        var candidates = new[] { Path.Combine(DataSourceFolder, file), Path.Combine(MacroFolder, file) };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private void LoadDataSource(string file)
    {
        var path = ResolveDataSourcePath(file);
        if (!File.Exists(path)) throw new MacroRuntimeException($"Datasource file not found: {path}");
        try
        {
            _dataRows = Csv.Parse(File.ReadAllText(path, Encoding.UTF8), DataSourceDelimiter);
        }
        catch (FormatException ex)
        {
            throw new MacroRuntimeException($"Cannot read datasource {path}: {ex.Message}");
        }
        DataSourcePath = file;
    }

    private List<string>? CurrentRowOrNull()
    {
        if (_dataRows is null || DataSourceLine > _dataRows.Count) return null;
        return _dataRows[DataSourceLine - 1];
    }

    public int DataSourceRowCount => _dataRows?.Count ?? 0;

    private string GetColumn(int n)
    {
        if (_dataRows is null) throw new MacroRuntimeException($"!COL{n} used but no datasource set (SET !DATASOURCE file.csv)");
        if (DataSourceLine > _dataRows.Count)
            throw new MacroRuntimeException($"End of datasource reached: line {DataSourceLine} requested but {DataSourcePath} has {_dataRows.Count} line(s)");
        var row = _dataRows[DataSourceLine - 1];
        if (n < 1 || n > row.Count)
            throw new MacroRuntimeException($"!COL{n} does not exist on datasource line {DataSourceLine} ({row.Count} column(s))");
        return row[n - 1];
    }
}
