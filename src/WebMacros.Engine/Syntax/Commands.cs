namespace WebMacros.Engine.Syntax;

/// <summary>
/// Base type of every parsed macro command. String-valued properties hold the value exactly as written
/// (quotes removed) — <c>{{variables}}</c> and <c>&lt;SP&gt;</c>-style escapes are resolved at run time.
/// </summary>
public abstract record MacroCommand
{
    public int LineNumber { get; init; }
    public string Text { get; init; } = "";
    public abstract string Name { get; }
}

public sealed record VersionCommand(string? Build) : MacroCommand { public override string Name => "VERSION"; }

public enum TabAction { Open, Switch, Close, CloseAllOthers }
public sealed record TabCommand(TabAction Action, string? Index) : MacroCommand { public override string Name => "TAB"; }

public sealed record UrlCommand(string Goto) : MacroCommand { public override string Name => "URL"; }
public sealed record BackCommand : MacroCommand { public override string Name => "BACK"; }
public sealed record RefreshCommand : MacroCommand { public override string Name => "REFRESH"; }
public sealed record WaitCommand(string Seconds) : MacroCommand { public override string Name => "WAIT"; }

/// <summary><c>SET name value</c>. When <see cref="EvalScript"/> is non-null the value was <c>EVAL("js")</c>.</summary>
public sealed record SetCommand(string Variable, string Value, string? EvalScript) : MacroCommand
{
    public override string Name => "SET";
    public bool IsEval => EvalScript is not null;
}

public sealed record AddCommand(string Variable, string Value) : MacroCommand { public override string Name => "ADD"; }

public sealed record TagCommand : MacroCommand
{
    public override string Name => "TAG";
    public string? Pos { get; init; }
    public string? Type { get; init; }
    public string? Attr { get; init; }
    public string? Form { get; init; }
    public string? XPath { get; init; }
    public string? Selector { get; init; }
    public string? Content { get; init; }
    public string? Extract { get; init; }
}

public sealed record EventCommand : MacroCommand
{
    public override string Name => "EVENT";
    public string Type { get; init; } = "CLICK";
    public string? Selector { get; init; }
    public string? XPath { get; init; }
    public string? Key { get; init; }
    public string? Chars { get; init; }
    public string? Point { get; init; }
    public string? Button { get; init; }
    public string? Modifiers { get; init; }
}

public sealed record ClickCommand(string X, string Y, string? Content) : MacroCommand { public override string Name => "CLICK"; }

/// <summary><c>FRAME F=n</c> or <c>FRAME NAME=name</c>.</summary>
public sealed record FrameCommand(string? Index, string? FrameName) : MacroCommand { public override string Name => "FRAME"; }

public sealed record PromptCommand(string Message, string? Variable, string? Default) : MacroCommand { public override string Name => "PROMPT"; }
public sealed record PauseCommand : MacroCommand { public override string Name => "PAUSE"; }
public sealed record SaveAsCommand(string Type, string? Folder, string? File) : MacroCommand { public override string Name => "SAVEAS"; }
public sealed record ScreenshotCommand(string Type, string? Folder, string? File) : MacroCommand { public override string Name => "SCREENSHOT"; }
public sealed record OnDialogCommand(string Pos, string Button, string? Content) : MacroCommand { public override string Name => "ONDIALOG"; }
public sealed record ClearCommand(string? What) : MacroCommand { public override string Name => "CLEAR"; }
public sealed record FilterCommand(string Type, string Status) : MacroCommand { public override string Name => "FILTER"; }

public enum StopwatchAction { Toggle, Start, Stop, Label }
public sealed record StopwatchCommand(StopwatchAction Action, string? Id, string? Label) : MacroCommand { public override string Name => "STOPWATCH"; }

/// <summary><c>SEARCH SOURCE=TXT:text|REGEXP:pattern [IGNORE_CASE=YES] [EXTRACT=$1]</c> over the page source.</summary>
public sealed record SearchCommand(string Source, string? Extract, string? IgnoreCase) : MacroCommand { public override string Name => "SEARCH"; }

public sealed record FileDeleteCommand(string FileName) : MacroCommand { public override string Name => "FILEDELETE"; }

/// <summary>A recognised iMacros command that WebMacros parses but does not implement (skipped with a warning).</summary>
public sealed record UnsupportedCommand(string CommandName) : MacroCommand { public override string Name => CommandName; }

/// <summary>A parsed macro: executable commands (comments and blank lines removed) plus the source lines.</summary>
public sealed record ParsedMacro(IReadOnlyList<MacroCommand> Commands, IReadOnlyList<string> SourceLines);
