using System.Globalization;
using System.Text.RegularExpressions;

namespace WebMacros.Engine.Syntax;

/// <summary>Parses iMacros (.iim) style macro text into <see cref="MacroCommand"/> objects.</summary>
public static partial class MacroParser
{
    /// <summary>iMacros commands that are recognised but not implemented by WebMacros.</summary>
    public static readonly IReadOnlySet<string> UnsupportedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SIZE", "DS", "WINCLICK", "IMAGECLICK", "IMAGESEARCH", "PROXY", "ONDOWNLOAD", "ONLOGIN",
        "ONCERTIFICATEDIALOG", "ONERRORDIALOG", "ONSECURITYDIALOG", "ONWEBPAGEDIALOG", "ONPRINT",
        "PRINT", "SAVEITEM", "CMDLINE", "DISCONNECT", "REDIAL", "EXTRACT"
    };

    public static readonly IReadOnlyList<string> SupportedCommands = new[]
    {
        "VERSION", "TAB", "URL", "BACK", "REFRESH", "WAIT", "SET", "ADD", "TAG", "EVENT", "EVENTS", "CLICK",
        "FRAME", "PROMPT", "PAUSE", "SAVEAS", "SCREENSHOT", "ONDIALOG", "CLEAR", "FILTER", "STOPWATCH",
        "SEARCH", "FILEDELETE"
    };

    [GeneratedRegex(@"^EVAL\s*\((?<arg>.*)\)$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex EvalRegex();

    [GeneratedRegex(@"\{\{[^}]*\}\}")]
    private static partial Regex VariableRefRegex();

    /// <summary>Parses a whole macro. Throws <see cref="MacroSyntaxException"/> on the first invalid line.</summary>
    public static ParsedMacro Parse(string text)
    {
        var lines = SplitLines(text);
        var commands = new List<MacroCommand>();
        for (var i = 0; i < lines.Count; i++)
        {
            var cmd = ParseLine(lines[i], i + 1);
            if (cmd is not null) commands.Add(cmd);
        }
        return new ParsedMacro(commands, lines);
    }

    /// <summary>Parses the macro and returns all syntax errors instead of throwing.</summary>
    public static IReadOnlyList<MacroSyntaxException> Validate(string text)
    {
        var errors = new List<MacroSyntaxException>();
        var lines = SplitLines(text);
        for (var i = 0; i < lines.Count; i++)
        {
            try { ParseLine(lines[i], i + 1); }
            catch (MacroSyntaxException ex) { errors.Add(ex); }
        }
        return errors;
    }

    public static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    public static bool IsCommentOrBlank(string line)
    {
        var t = line.Trim();
        return t.Length == 0 || t.StartsWith('\'') || t.StartsWith("//", StringComparison.Ordinal);
    }

    /// <summary>Parses a single line; returns null for comments and blank lines.</summary>
    public static MacroCommand? ParseLine(string line, int lineNumber = 1)
    {
        if (IsCommentOrBlank(line)) return null;
        try
        {
            var (command, tokens) = Tokenizer.TokenizeLine(line.Trim());
            var args = new Args(command, tokens);
            MacroCommand result = command switch
            {
                "VERSION" => new VersionCommand(args.Optional("BUILD")) { },
                "TAB" => ParseTab(args),
                "URL" => new UrlCommand(args.Required("GOTO")),
                "BACK" => args.NoArgs(new BackCommand()),
                "REFRESH" => args.NoArgs(new RefreshCommand()),
                "WAIT" => ParseWait(args),
                "SET" => ParseSet(args),
                "ADD" => ParseAdd(args),
                "TAG" => ParseTag(args),
                "EVENT" or "EVENTS" => ParseEvent(args),
                "CLICK" => ParseClick(args),
                "FRAME" => ParseFrame(args),
                "PROMPT" => ParsePrompt(args),
                "PAUSE" => args.NoArgs(new PauseCommand()),
                "SAVEAS" => ParseSaveAs(args),
                "SCREENSHOT" => ParseScreenshot(args),
                "ONDIALOG" => ParseOnDialog(args),
                "CLEAR" => ParseClear(args),
                "FILTER" => ParseFilter(args),
                "STOPWATCH" => ParseStopwatch(args),
                "SEARCH" => ParseSearch(args),
                "FILEDELETE" => new FileDeleteCommand(args.Required("NAME")),
                _ when UnsupportedCommands.Contains(command) => new UnsupportedCommand(command),
                _ => throw new MacroSyntaxException($"Unknown command \"{command}\"")
            };
            return result with { LineNumber = lineNumber, Text = line.Trim() };
        }
        catch (MacroSyntaxException ex) when (ex.LineNumber == 0)
        {
            throw ex.WithLine(lineNumber);
        }
    }

    private static bool HasVariables(string s) => VariableRefRegex().IsMatch(s);

    private static void CheckNumber(string command, string key, string value, bool allowDecimal = true)
    {
        if (HasVariables(value)) return;
        var ok = allowDecimal
            ? double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        if (!ok) throw new MacroSyntaxException($"{command}: {key}= must be a number, got \"{value}\"");
    }

    private static void CheckOneOf(string command, string key, string value, params string[] allowed)
    {
        if (HasVariables(value)) return;
        if (!allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
            throw new MacroSyntaxException($"{command}: {key}= must be one of {string.Join("|", allowed)}, got \"{value}\"");
    }

    private static MacroCommand ParseTab(Args a)
    {
        a.AllowKeys("T");
        if (a.Named.TryGetValue("T", out var t))
        {
            CheckNumber("TAB", "T", t, allowDecimal: false);
            return new TabCommand(TabAction.Switch, t);
        }
        var word = a.Positional.FirstOrDefault()?.ToUpperInvariant();
        return word switch
        {
            "OPEN" => new TabCommand(TabAction.Open, null),
            "CLOSE" => new TabCommand(TabAction.Close, null),
            "CLOSEALLOTHERS" => new TabCommand(TabAction.CloseAllOthers, null),
            _ => throw new MacroSyntaxException("TAB requires T=n, OPEN, CLOSE or CLOSEALLOTHERS")
        };
    }

    private static MacroCommand ParseWait(Args a)
    {
        a.AllowKeys("SECONDS");
        var s = a.Required("SECONDS");
        CheckNumber("WAIT", "SECONDS", s);
        return new WaitCommand(s);
    }

    private static MacroCommand ParseSet(Args a)
    {
        if (a.Tokens.Count != 2)
            throw new MacroSyntaxException("SET requires exactly two parameters: SET <variable> <value> (use <SP> or quotes for spaces)");
        var name = a.Tokens[0].Raw;
        ValidateVariableName("SET", name);
        var valueToken = a.Tokens[1];
        var (value, _) = Tokenizer.Unquote(valueToken.Raw);
        string? eval = null;
        var m = EvalRegex().Match(valueToken.Raw);
        if (m.Success && !Tokenizer.Unquote(valueToken.Raw).Quoted)
        {
            var arg = m.Groups["arg"].Value.Trim();
            var (inner, quoted) = Tokenizer.Unquote(arg);
            if (!quoted) throw new MacroSyntaxException("EVAL requires a quoted JavaScript string: EVAL(\"...\")");
            eval = inner;
        }
        return new SetCommand(name, value, eval);
    }

    private static MacroCommand ParseAdd(Args a)
    {
        if (a.Tokens.Count != 2)
            throw new MacroSyntaxException("ADD requires exactly two parameters: ADD <variable> <value>");
        var name = a.Tokens[0].Raw;
        ValidateVariableName("ADD", name);
        return new AddCommand(name, Tokenizer.Unquote(a.Tokens[1].Raw).Value);
    }

    [GeneratedRegex(@"^(!?[A-Za-z_][A-Za-z0-9_]*)$")]
    private static partial Regex VariableNameRegex();

    private static void ValidateVariableName(string command, string name)
    {
        if (!VariableNameRegex().IsMatch(name))
            throw new MacroSyntaxException($"{command}: invalid variable name \"{name}\"");
    }

    private static MacroCommand ParseTag(Args a)
    {
        a.AllowKeys("POS", "TYPE", "ATTR", "FORM", "XPATH", "SELECTOR", "CONTENT", "EXTRACT");
        var tag = new TagCommand
        {
            Pos = a.Optional("POS"),
            Type = a.Optional("TYPE"),
            Attr = a.Optional("ATTR"),
            Form = a.Optional("FORM"),
            XPath = a.Optional("XPATH"),
            Selector = a.Optional("SELECTOR"),
            Content = a.Optional("CONTENT"),
            Extract = a.Optional("EXTRACT"),
        };
        if (a.Positional.Count > 0) throw new MacroSyntaxException($"TAG: unexpected parameter \"{a.Positional[0]}\"");
        var modes = (tag.XPath is null ? 0 : 1) + (tag.Selector is null ? 0 : 1);
        if (modes > 1) throw new MacroSyntaxException("TAG: use either XPATH= or SELECTOR=, not both");
        if (modes == 0 && tag.Type is null) throw new MacroSyntaxException("TAG requires TYPE= (or XPATH= / SELECTOR=)");
        if (modes == 1 && (tag.Type is not null || tag.Attr is not null))
            throw new MacroSyntaxException("TAG: TYPE=/ATTR= cannot be combined with XPATH=/SELECTOR=");
        if (tag.Content is not null && tag.Extract is not null) throw new MacroSyntaxException("TAG: CONTENT= and EXTRACT= are mutually exclusive");
        if (tag.Pos is not null && !HasVariables(tag.Pos) && !Scripting.TagSpecBuilder.TryParsePos(tag.Pos, out _, out _))
            throw new MacroSyntaxException($"TAG: invalid POS=\"{tag.Pos}\" (expected n or Rn)");
        return tag;
    }

    private static MacroCommand ParseEvent(Args a)
    {
        a.AllowKeys("TYPE", "SELECTOR", "XPATH", "KEY", "CHARS", "POINT", "BUTTON", "MODIFIERS", "KEYS");
        var type = a.Required("TYPE").ToUpperInvariant();
        CheckOneOf("EVENT", "TYPE", type, "CLICK", "DBLCLICK", "KEYPRESS", "KEYDOWN", "KEYUP", "MOUSEOVER", "MOUSEDOWN", "MOUSEUP", "MOUSEMOVE", "FOCUS", "BLUR", "CHANGE", "INPUT", "SUBMIT");
        var ev = new EventCommand
        {
            Type = type,
            Selector = a.Optional("SELECTOR"),
            XPath = a.Optional("XPATH"),
            Key = a.Optional("KEY"),
            Chars = a.Optional("CHARS") ?? a.Optional("KEYS"),
            Point = a.Optional("POINT"),
            Button = a.Optional("BUTTON"),
            Modifiers = a.Optional("MODIFIERS"),
        };
        if (ev.Selector is not null && ev.XPath is not null) throw new MacroSyntaxException("EVENT: use either SELECTOR= or XPATH=, not both");
        if (ev.Key is not null) CheckNumber("EVENT", "KEY", ev.Key, allowDecimal: false);
        return ev;
    }

    private static MacroCommand ParseClick(Args a)
    {
        a.AllowKeys("X", "Y", "CONTENT");
        var x = a.Required("X");
        var y = a.Required("Y");
        CheckNumber("CLICK", "X", x);
        CheckNumber("CLICK", "Y", y);
        return new ClickCommand(x, y, a.Optional("CONTENT"));
    }

    private static MacroCommand ParseFrame(Args a)
    {
        a.AllowKeys("F", "NAME");
        var f = a.Optional("F");
        var name = a.Optional("NAME");
        if ((f is null) == (name is null)) throw new MacroSyntaxException("FRAME requires exactly one of F=n or NAME=name");
        if (f is not null) CheckNumber("FRAME", "F", f, allowDecimal: false);
        return new FrameCommand(f, name);
    }

    private static MacroCommand ParsePrompt(Args a)
    {
        if (a.Tokens.Count is < 1 or > 3) throw new MacroSyntaxException("PROMPT syntax: PROMPT <message> [<variable> [<default>]]");
        var msg = Tokenizer.Unquote(a.Tokens[0].Raw).Value;
        string? variable = null, def = null;
        if (a.Tokens.Count >= 2)
        {
            variable = a.Tokens[1].Raw;
            ValidateVariableName("PROMPT", variable);
        }
        if (a.Tokens.Count == 3) def = Tokenizer.Unquote(a.Tokens[2].Raw).Value;
        return new PromptCommand(msg, variable, def);
    }

    private static MacroCommand ParseSaveAs(Args a)
    {
        a.AllowKeys("TYPE", "FOLDER", "FILE");
        var type = a.Required("TYPE").ToUpperInvariant();
        CheckOneOf("SAVEAS", "TYPE", type, "EXTRACT", "CPL", "HTM", "TXT", "PNG", "JPG", "MHT", "BMP");
        return new SaveAsCommand(type, a.Optional("FOLDER"), a.Optional("FILE"));
    }

    private static MacroCommand ParseScreenshot(Args a)
    {
        a.AllowKeys("TYPE", "FOLDER", "FILE");
        var type = a.Required("TYPE").ToUpperInvariant();
        CheckOneOf("SCREENSHOT", "TYPE", type, "PAGE", "BROWSER");
        return new ScreenshotCommand(type, a.Optional("FOLDER"), a.Optional("FILE"));
    }

    private static MacroCommand ParseOnDialog(Args a)
    {
        a.AllowKeys("POS", "BUTTON", "CONTENT");
        var pos = a.Optional("POS") ?? "1";
        CheckNumber("ONDIALOG", "POS", pos, allowDecimal: false);
        var button = a.Required("BUTTON").ToUpperInvariant();
        CheckOneOf("ONDIALOG", "BUTTON", button, "OK", "CANCEL", "YES", "NO");
        return new OnDialogCommand(pos, button, a.Optional("CONTENT"));
    }

    private static MacroCommand ParseClear(Args a)
    {
        a.AllowKeys();
        var what = a.Positional.FirstOrDefault()?.ToUpperInvariant();
        if (what is not null) CheckOneOf("CLEAR", "", what, "COOKIES", "CACHE", "ALL");
        return new ClearCommand(what);
    }

    private static MacroCommand ParseFilter(Args a)
    {
        a.AllowKeys("TYPE", "STATUS");
        var type = a.Required("TYPE").ToUpperInvariant();
        CheckOneOf("FILTER", "TYPE", type, "IMAGES", "FLASH", "POPUPS", "NONE");
        var status = (a.Optional("STATUS") ?? "ON").ToUpperInvariant();
        CheckOneOf("FILTER", "STATUS", status, "ON", "OFF");
        return new FilterCommand(type, status);
    }

    private static MacroCommand ParseStopwatch(Args a)
    {
        a.AllowKeys("ID", "LABEL");
        var id = a.Optional("ID");
        var label = a.Optional("LABEL");
        var word = a.Positional.FirstOrDefault()?.ToUpperInvariant();
        if (label is not null) return new StopwatchCommand(StopwatchAction.Label, id, label);
        if (id is null) throw new MacroSyntaxException("STOPWATCH requires ID=name (or LABEL=name)");
        return word switch
        {
            null => new StopwatchCommand(StopwatchAction.Toggle, id, null),
            "START" => new StopwatchCommand(StopwatchAction.Start, id, null),
            "STOP" => new StopwatchCommand(StopwatchAction.Stop, id, null),
            _ => throw new MacroSyntaxException($"STOPWATCH: unexpected \"{word}\" (expected START or STOP)")
        };
    }

    private static MacroCommand ParseSearch(Args a)
    {
        a.AllowKeys("SOURCE", "EXTRACT", "IGNORE_CASE");
        var source = a.Required("SOURCE");
        if (!HasVariables(source) && !(source.StartsWith("TXT:", StringComparison.OrdinalIgnoreCase) || source.StartsWith("REGEXP:", StringComparison.OrdinalIgnoreCase)))
            throw new MacroSyntaxException("SEARCH: SOURCE= must start with TXT: or REGEXP:");
        return new SearchCommand(source, a.Optional("EXTRACT"), a.Optional("IGNORE_CASE"));
    }

    /// <summary>Helper giving keyed and positional access to a line's tokens.</summary>
    private sealed class Args
    {
        public string Command { get; }
        public IReadOnlyList<Token> Tokens { get; }
        public Dictionary<string, string> Named { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Positional { get; } = new();

        public Args(string command, IReadOnlyList<Token> tokens)
        {
            Command = command;
            Tokens = tokens;
            foreach (var t in tokens)
            {
                if (t.Key is null) Positional.Add(t.Value);
                else if (!Named.TryAdd(t.Key, t.Value))
                    throw new MacroSyntaxException($"{command}: duplicate parameter {t.Key}=");
            }
        }

        public string Required(string key) =>
            Named.TryGetValue(key, out var v) ? v : throw new MacroSyntaxException($"{Command} requires {key}=");

        public string? Optional(string key) => Named.TryGetValue(key, out var v) ? v : null;

        public void AllowKeys(params string[] keys)
        {
            foreach (var k in Named.Keys)
                if (!keys.Contains(k, StringComparer.OrdinalIgnoreCase))
                    throw new MacroSyntaxException($"{Command}: unknown parameter {k}=");
        }

        public T NoArgs<T>(T cmd) where T : MacroCommand
        {
            if (Tokens.Count > 0) throw new MacroSyntaxException($"{Command} takes no parameters");
            return cmd;
        }
    }
}
