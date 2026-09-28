using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WebMacros.Engine.Scripting;
using WebMacros.Engine.Syntax;

namespace WebMacros.Engine.Runtime;

/// <summary>Executes parsed macros against an <see cref="IBrowserDriver"/>.</summary>
public sealed partial class MacroInterpreter
{
    private readonly IBrowserDriver _driver;
    private readonly IMacroHost _host;
    private readonly InterpreterOptions _options;
    private readonly IEngineClock _clock;

    private MacroState _state = null!;
    private CancellationToken _ct;
    private readonly SortedDictionary<int, DialogDecision> _dialogQueue = new();
    private readonly Dictionary<string, DateTimeOffset> _stopwatches = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _macroStart;

    public MacroInterpreter(IBrowserDriver driver, IMacroHost? host = null, InterpreterOptions? options = null, IEngineClock? clock = null)
    {
        _driver = driver;
        _host = host ?? new NullMacroHost();
        _options = options ?? new InterpreterOptions();
        _clock = clock ?? SystemClock.Instance;
    }

    /// <summary>State of the last/current run (variables, extract). Exposed for tests and the UI.</summary>
    public MacroState State => _state;

    public bool IsRunning { get; private set; }

    public Task<PlayResult> PlayAsync(string macroText, int loops = 1, CancellationToken ct = default, MacroRunOptions? run = null)
    {
        ParsedMacro parsed;
        try
        {
            parsed = MacroParser.Parse(macroText);
        }
        catch (MacroSyntaxException ex)
        {
            _host.Log(LogLevel.Error, ex.Message);
            return Task.FromResult(new PlayResult { Status = PlayStatus.Failed, ErrorLine = ex.LineNumber, ErrorMessage = ex.Reason });
        }
        return PlayAsync(parsed, loops, ct, run);
    }

    public async Task<PlayResult> PlayAsync(ParsedMacro macro, int loops = 1, CancellationToken ct = default, MacroRunOptions? run = null)
    {
        if (IsRunning) throw new InvalidOperationException("A macro is already running");
        IsRunning = true;
        _ct = ct;
        _state = new MacroState(_options, _clock, () => _driver.CurrentUrl);
        _dialogQueue.Clear();
        _stopwatches.Clear();
        _macroStart = _clock.Now;
        var sw = Stopwatch.StartNew();
        var extracts = new List<string>();
        var completed = 0;
        var maxLoops = Math.Max(1, loops);
        _driver.DialogHandler = HandleDialog;
        try
        {
            var loopValue = 1;
            var first = true;
            _state.LoopChanged += v => loopValue = v;
            if (run is not null && !ApplyRunOptions(run, out var presetError))
                return Finish(PlayStatus.Failed, completed, extracts, sw, null, presetError);
            while (loopValue <= maxLoops || first)
            {
                _state.Loop = loopValue;
                _state.IsFirstLoop = first;
                _state.ClearExtract();
                if (maxLoops > 1) _host.Log(LogLevel.Info, $"--- Loop {loopValue} of {maxLoops} ---");

                foreach (var cmd in macro.Commands)
                {
                    ct.ThrowIfCancellationRequested();
                    _host.OnLine(cmd.LineNumber, cmd.Text, _state.Loop);
                    try
                    {
                        await ExecuteAsync(cmd).ConfigureAwait(true);
                    }
                    catch (MacroRuntimeException ex) when (ex is not MacroAbortException && _state.ErrorIgnore)
                    {
                        _host.Log(LogLevel.Warning, $"Line {cmd.LineNumber}: {ex.Message} (ignored, !ERRORIGNORE YES)");
                    }
                    catch (MacroRuntimeException ex) when (ex is not MacroAbortException && _state.ErrorLoop && maxLoops > 1)
                    {
                        _host.Log(LogLevel.Warning, $"Line {cmd.LineNumber}: {ex.Message} (skipping to next loop, !ERRORLOOP YES)");
                        break;
                    }
                    catch (MacroRuntimeException ex)
                    {
                        _host.Log(LogLevel.Error, $"Line {cmd.LineNumber}: {ex.Message}");
                        return Finish(PlayStatus.Failed, completed, extracts, sw, cmd.LineNumber, ex.Message);
                    }
                    catch (MacroSyntaxException ex)
                    {
                        _host.Log(LogLevel.Error, $"Line {cmd.LineNumber}: {ex.Reason}");
                        return Finish(PlayStatus.Failed, completed, extracts, sw, cmd.LineNumber, ex.Reason);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (TimeoutException ex)
                    {
                        var e = new MacroRuntimeException(ex.Message, cmd.LineNumber);
                        if (_state.ErrorIgnore) { _host.Log(LogLevel.Warning, $"Line {cmd.LineNumber}: {ex.Message} (ignored)"); continue; }
                        _host.Log(LogLevel.Error, $"Line {cmd.LineNumber}: {e.Message}");
                        return Finish(PlayStatus.Failed, completed, extracts, sw, cmd.LineNumber, ex.Message);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
                    {
                        var msg = ex.GetType().Name + ": " + ex.Message;
                        if (_state.ErrorIgnore) { _host.Log(LogLevel.Warning, $"Line {cmd.LineNumber}: {msg} (ignored)"); continue; }
                        _host.Log(LogLevel.Error, $"Line {cmd.LineNumber}: {msg}");
                        return Finish(PlayStatus.Failed, completed, extracts, sw, cmd.LineNumber, msg);
                    }
                }

                var extract = _state.Extract;
                if (extract.Length > 0)
                {
                    extracts.Add(extract);
                    if (_state.ExtractTestPopup) await _host.ShowExtractAsync(extract, ct).ConfigureAwait(true);
                }
                completed++;
                first = false;
                loopValue++;
                if (maxLoops == 1) break;
            }
            return Finish(PlayStatus.Completed, completed, extracts, sw, null, null);
        }
        catch (OperationCanceledException)
        {
            _host.Log(LogLevel.Warning, "Macro stopped");
            return Finish(PlayStatus.Stopped, completed, extracts, sw, null, "Stopped by user");
        }
        finally
        {
            _driver.DialogHandler = null;
            IsRunning = false;
        }
    }

    /// <summary>Applies variables set with iimSet (and other per-run settings) before the first line runs.</summary>
    private bool ApplyRunOptions(MacroRunOptions run, out string? error)
    {
        error = null;
        if (run.ShowExtractPopup is bool popup) _state.Set("!EXTRACT_TEST_POPUP", popup ? "YES" : "NO");
        foreach (var (name, value) in run.Variables)
        {
            try
            {
                var warning = _state.Set(name, value);
                if (warning is not null) _host.Log(LogLevel.Warning, $"iimSet {name}: {warning}");
            }
            catch (MacroRuntimeException ex)
            {
                error = $"iimSet(\"{name}\"): {ex.Message}";
                _host.Log(LogLevel.Error, error);
                return false;
            }
        }
        return true;
    }

    private PlayResult Finish(PlayStatus status, int completed, List<string> extracts, Stopwatch sw, int? line, string? message)
    {
        var r = new PlayResult
        {
            Status = status, LoopsCompleted = completed, Extracts = extracts.ToArray(), Elapsed = sw.Elapsed,
            ErrorLine = line, ErrorMessage = message,
        };
        if (status == PlayStatus.Completed) _host.Log(LogLevel.Info, r.ToString());
        return r;
    }

    private TimeSpan StepTimeout => TimeSpan.FromSeconds(_state.TimeoutStep);
    private TimeSpan PageTimeout => TimeSpan.FromSeconds(_state.TimeoutPage);

    private string R(string? s) => _state.Resolve(s);
    private string? RO(string? s) => _state.ResolveOptional(s);

    private async Task ExecuteAsync(MacroCommand cmd)
    {
        switch (cmd)
        {
            case VersionCommand v:
                if (v.Build is not null) _host.Log(LogLevel.Info, $"Macro recorded with build {v.Build} (WebMacros {_options.Version})");
                break;
            case TabCommand t: await ExecTabAsync(t); break;
            case UrlCommand u: await ExecUrlAsync(u); break;
            case BackCommand:
                _state.Frame = null;
                await _driver.GoBackAsync(PageTimeout, _ct);
                break;
            case RefreshCommand:
                _state.Frame = null;
                await _driver.ReloadAsync(PageTimeout, _ct);
                break;
            case WaitCommand w:
                var secs = ParseDouble("WAIT SECONDS", R(w.Seconds));
                await _clock.Delay(TimeSpan.FromSeconds(secs), _ct);
                break;
            case SetCommand s: await ExecSetAsync(s); break;
            case AddCommand a: ExecAdd(a); break;
            case TagCommand tag: await ExecTagAsync(tag); break;
            case EventCommand e: await ExecEventAsync(e); break;
            case ClickCommand c: await ExecClickAsync(c); break;
            case FrameCommand f: await ExecFrameAsync(f); break;
            case PromptCommand p: await ExecPromptAsync(p); break;
            case PauseCommand:
                _host.Log(LogLevel.Info, "Paused — press Resume to continue");
                await _host.PauseAsync(_ct);
                break;
            case SaveAsCommand sa: await ExecSaveAsAsync(sa); break;
            case ScreenshotCommand sc: await ExecScreenshotAsync(sc); break;
            case OnDialogCommand od:
                var pos = ParseInt("ONDIALOG POS", R(od.Pos));
                var accept = od.Button is "OK" or "YES";
                _dialogQueue[pos] = new DialogDecision(accept, RO(od.Content));
                break;
            case ClearCommand cl:
                var kinds = cl.What switch { "COOKIES" => BrowsingDataKinds.Cookies, "CACHE" => BrowsingDataKinds.Cache, _ => BrowsingDataKinds.All };
                await _driver.ClearBrowsingDataAsync(kinds, _ct);
                break;
            case FilterCommand fi:
                if (fi.Type == "IMAGES") await _driver.SetImageBlockingAsync(fi.Status == "ON", _ct);
                else if (fi.Type == "NONE") await _driver.SetImageBlockingAsync(false, _ct);
                else _host.Log(LogLevel.Warning, $"FILTER TYPE={fi.Type} is not implemented; ignored");
                break;
            case StopwatchCommand st: ExecStopwatch(st); break;
            case SearchCommand se: await ExecSearchAsync(se); break;
            case FileDeleteCommand fd:
                var path = ResolveFilePath(R(fd.FileName), _state.DownloadFolder);
                if (!File.Exists(path)) throw new MacroRuntimeException($"FILEDELETE: file not found: {path}");
                File.Delete(path);
                break;
            case UnsupportedCommand un:
                _host.Log(LogLevel.Warning, $"Line {un.LineNumber}: {un.CommandName} is not implemented in WebMacros; skipped");
                break;
            default:
                throw new MacroRuntimeException($"Unhandled command {cmd.Name}");
        }
    }

    private static double ParseDouble(string what, string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 0
            ? d : throw new MacroRuntimeException($"{what} must be a non-negative number, got \"{value}\"");

    private static int ParseInt(string what, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i : throw new MacroRuntimeException($"{what} must be an integer, got \"{value}\"");

    // ---- TAB / URL -------------------------------------------------------------------------------------------

    private async Task ExecTabAsync(TabCommand t)
    {
        switch (t.Action)
        {
            case TabAction.Open: await _driver.OpenTabAsync(_ct); break;
            case TabAction.Close:
                if (_driver.TabCount <= 1) throw new MacroRuntimeException("TAB CLOSE: cannot close the last tab");
                await _driver.CloseTabAsync(_ct);
                _state.Frame = null;
                break;
            case TabAction.CloseAllOthers: await _driver.CloseOtherTabsAsync(_ct); break;
            case TabAction.Switch:
                var n = ParseInt("TAB T", R(t.Index));
                if (n < 1 || n > _driver.TabCount) throw new MacroRuntimeException($"TAB T={n}: tab does not exist ({_driver.TabCount} open)");
                await _driver.SwitchTabAsync(n - 1, _ct);
                _state.Frame = null;
                break;
        }
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex SchemeRegex();

    public static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (url.Length == 0) throw new MacroRuntimeException("URL GOTO= is empty");
        if (SchemeRegex().IsMatch(url) && !Regex.IsMatch(url, @"^[A-Za-z0-9.-]+:\d+(/|$)")) return url;
        return "https://" + url;
    }

    private async Task ExecUrlAsync(UrlCommand u)
    {
        var url = R(u.Goto);
        if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            await _driver.ExecuteScriptAsync(url["javascript:".Length..], _ct);
            await _driver.WaitForPageLoadAsync(PageTimeout, _ct);
            return;
        }
        _state.Frame = null;
        await _driver.NavigateAsync(NormalizeUrl(url), PageTimeout, _ct);
    }

    // ---- SET / ADD -------------------------------------------------------------------------------------------

    private async Task ExecSetAsync(SetCommand s)
    {
        string value;
        if (s.IsEval)
        {
            var code = R(s.EvalScript);
            var raw = await _driver.ExecuteScriptAsync(ScriptBuilder.BuildEvalScript(code), _ct);
            var res = ScriptBuilder.ParseResult<EvalResult>(raw)
                      ?? throw new MacroRuntimeException("EVAL: the browser returned no result (is a page loaded?)");
            if (!res.Ok)
            {
                if (res.MacroError) throw new MacroAbortException("MacroError: " + res.Error);
                throw new MacroRuntimeException("EVAL error: " + res.Error);
            }
            value = res.Value ?? "";
        }
        else value = R(s.Value);

        var warning = _state.Set(s.Variable, value);
        if (warning is not null) _host.Log(LogLevel.Warning, warning);
        if (s.Variable.Equals("!EXTRACT", StringComparison.OrdinalIgnoreCase) && !value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            _host.OnExtract(value);
    }

    private void ExecAdd(AddCommand a)
    {
        var value = R(a.Value);
        if (a.Variable.Equals("!EXTRACT", StringComparison.OrdinalIgnoreCase))
        {
            AddExtract(value);
            return;
        }
        var current = _state.IsDefined(a.Variable) ? _state.Get(a.Variable) : "";
        string result;
        if (double.TryParse(current.Length == 0 ? "0" : current, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            result = (x + y).ToString(CultureInfo.InvariantCulture);
        else
            result = current + value;
        var warning = _state.Set(a.Variable, result);
        if (warning is not null) _host.Log(LogLevel.Warning, warning);
    }

    private void AddExtract(string value)
    {
        _state.AddExtract(value);
        _host.OnExtract(value);
        _host.Log(LogLevel.Extract, value);
    }

    // ---- Element commands ------------------------------------------------------------------------------------

    /// <summary>Runs an element script, retrying "not found" results until !TIMEOUT_STEP elapses.</summary>
    private async Task<ElementResult> RunElementScriptAsync(ElementSpec spec, string what)
    {
        var script = ScriptBuilder.BuildElementScript(spec);
        var deadline = _clock.Now + StepTimeout;
        string lastError = "Element not found";
        while (true)
        {
            _ct.ThrowIfCancellationRequested();
            ElementResult? res = null;
            try
            {
                var raw = await _driver.ExecuteScriptAsync(script, _ct);
                res = ScriptBuilder.ParseResult<ElementResult>(raw);
                if (res is null) lastError = "Page is not ready (no script result)";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not MacroRuntimeException)
            {
                lastError = "Script failed: " + ex.Message; // e.g. page navigating — retry
            }
            if (res is { Ok: true }) return res;
            if (res is { Ok: false, Retry: false }) throw new MacroRuntimeException($"{what}: {res.Error}");
            if (res is not null) lastError = res.Error ?? lastError;
            if (_clock.Now >= deadline)
                return new ElementResult { Ok = false, Found = false, Error = $"{lastError} (after {_state.TimeoutStep:0.#}s, !TIMEOUT_STEP)" };
            await _clock.Delay(_options.PollInterval, _ct);
        }
    }

    private async Task ExecTagAsync(TagCommand t)
    {
        var spec = TagSpecBuilder.BuildTag(RO(t.Pos), RO(t.Type), RO(t.Attr), RO(t.Form), RO(t.XPath), RO(t.Selector),
            RO(t.Content), RO(t.Extract), _state.Frame);
        var res = await RunElementScriptAsync(spec, "TAG");
        if (!res.Ok)
        {
            if (spec.Extract is not null && _state.ErrorIgnore)
            {
                _host.Log(LogLevel.Warning, $"Line {t.LineNumber}: {res.Error} — extracting #EANF#");
                AddExtract("#EANF#");
                return;
            }
            throw new MacroRuntimeException($"TAG: {res.Error}");
        }
        if (spec.Extract is not null)
        {
            AddExtract(res.Value ?? "");
            return;
        }
        await _driver.WaitForPageLoadAsync(PageTimeout, _ct);
    }

    private async Task ExecEventAsync(EventCommand e)
    {
        var spec = TagSpecBuilder.BuildEvent(e.Type, RO(e.Selector), RO(e.XPath), RO(e.Key), RO(e.Chars), _state.Frame);
        var res = await RunElementScriptAsync(spec, "EVENT");
        if (!res.Ok) throw new MacroRuntimeException($"EVENT: {res.Error}");
        await _driver.WaitForPageLoadAsync(PageTimeout, _ct);
    }

    private async Task ExecClickAsync(ClickCommand c)
    {
        var spec = TagSpecBuilder.BuildPoint(ParseDouble("CLICK X", R(c.X)), ParseDouble("CLICK Y", R(c.Y)), RO(c.Content), _state.Frame);
        var raw = await _driver.ExecuteScriptAsync(ScriptBuilder.BuildElementScript(spec), _ct);
        var res = ScriptBuilder.ParseResult<ElementResult>(raw);
        if (res is not { Ok: true }) throw new MacroRuntimeException($"CLICK: {res?.Error ?? "no result"}");
        await _driver.WaitForPageLoadAsync(PageTimeout, _ct);
    }

    private async Task ExecFrameAsync(FrameCommand f)
    {
        FrameSpec frame;
        if (f.Index is not null)
        {
            var n = ParseInt("FRAME F", R(f.Index));
            if (n < 0) throw new MacroRuntimeException("FRAME F= must be >= 0");
            frame = FrameSpec.ByIndex(n);
        }
        else frame = FrameSpec.ByName(R(f.FrameName));

        if (frame.IsTop) { _state.Frame = null; return; }
        var res = await RunElementScriptAsync(TagSpecBuilder.BuildFrameCheck(frame), "FRAME");
        if (!res.Ok) throw new MacroRuntimeException($"FRAME: {res.Error}");
        _state.Frame = frame;
    }

    // ---- UI --------------------------------------------------------------------------------------------------

    private async Task ExecPromptAsync(PromptCommand p)
    {
        var message = R(p.Message);
        if (p.Variable is null)
        {
            await _host.ShowMessageAsync(message, _ct);
            return;
        }
        var answer = await _host.PromptAsync(message, RO(p.Default), _ct);
        if (answer is null) throw new OperationCanceledException("Prompt cancelled");
        var warning = _state.Set(p.Variable, answer);
        if (warning is not null) _host.Log(LogLevel.Warning, warning);
    }

    private DialogDecision? HandleDialog(BrowserDialog dialog)
    {
        _host.Log(LogLevel.Info, $"Dialog ({dialog.Kind}): {dialog.Message}");
        if (_dialogQueue.Count == 0) return null;
        var firstKey = _dialogQueue.Keys.First();
        DialogDecision? decision = null;
        if (firstKey == 1)
        {
            decision = _dialogQueue[1];
            _dialogQueue.Remove(1);
        }
        // Shift the remaining positions down by one: POS=2 becomes the next dialog.
        var remaining = _dialogQueue.ToList();
        _dialogQueue.Clear();
        foreach (var (k, v) in remaining) if (k - 1 >= 1) _dialogQueue[k - 1] = v;
        if (decision is not null)
            _host.Log(LogLevel.Info, $"  → answered {(decision.Accept ? "OK" : "CANCEL")}{(decision.Text is null ? "" : " with \"" + decision.Text + "\"")} (ONDIALOG)");
        return decision;
    }

    // ---- Files -----------------------------------------------------------------------------------------------

    private string ResolveFolder(string? folder)
    {
        var f = folder is null ? "*" : R(folder);
        if (f == "*" || f.Length == 0) f = _state.DownloadFolder;
        else if (!Path.IsPathRooted(f)) f = Path.Combine(_state.DownloadFolder, f);
        Directory.CreateDirectory(f);
        return f;
    }

    private static string ResolveFilePath(string file, string folder) => Path.IsPathRooted(file) ? file : Path.Combine(folder, file);

    [GeneratedRegex(@"[^A-Za-z0-9._ -]+")]
    private static partial Regex UnsafeFileChars();

    private async Task<string> AutoNameAsync()
    {
        string title = "";
        try
        {
            title = ScriptBuilder.ParseResult<EvalResult>(await _driver.ExecuteScriptAsync(ScriptBuilder.PageTitleScript, _ct))?.Value ?? "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Uri.TryCreate(_driver.CurrentUrl, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host) ? u.Host : "page";
        }
        var safe = UnsafeFileChars().Replace(title, "_").Trim(' ', '.', '_');
        if (safe.Length > 80) safe = safe[..80];
        return safe.Length == 0 ? "page" : safe;
    }

    private async Task<string> BuildFileNameAsync(string? file, string ext, string autoBase)
    {
        var f = file is null ? "*" : R(file);
        string name;
        if (f == "*" || f.Length == 0) name = autoBase == "" ? await AutoNameAsync() : autoBase;
        else if (f.StartsWith('+')) name = (autoBase == "" ? await AutoNameAsync() : autoBase) + f[1..];
        else name = f;
        if (Path.GetExtension(name).Length == 0) name += ext;
        return name;
    }

    private async Task ExecSaveAsAsync(SaveAsCommand s)
    {
        var folder = ResolveFolder(s.Folder);
        switch (s.Type)
        {
            case "EXTRACT":
            {
                var path = ResolveFilePath(await BuildFileNameAsync(s.File, ".csv", "extract"), folder);
                if (_state.ExtractValues.Count == 0)
                {
                    _host.Log(LogLevel.Warning, "SAVEAS TYPE=EXTRACT: nothing extracted yet; no row written");
                    return;
                }
                await File.AppendAllTextAsync(path, Csv.FormatRow(_state.ExtractValues) + Environment.NewLine, new UTF8Encoding(false), _ct);
                _host.Log(LogLevel.Info, $"Extract appended to {path}");
                _state.ClearExtract();
                break;
            }
            case "HTM":
            case "CPL":
            case "TXT":
            {
                var script = s.Type == "TXT" ? ScriptBuilder.PageTextScript : ScriptBuilder.PageHtmlScript;
                var content = ScriptBuilder.ParseResult<EvalResult>(await _driver.ExecuteScriptAsync(script, _ct))?.Value
                              ?? throw new MacroRuntimeException("SAVEAS: could not read page content");
                var path = ResolveFilePath(await BuildFileNameAsync(s.File, s.Type == "TXT" ? ".txt" : ".htm", ""), folder);
                await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), _ct);
                _host.Log(LogLevel.Info, $"Page saved to {path}");
                break;
            }
            case "PNG":
            case "JPG":
            {
                var fmt = s.Type == "PNG" ? ImageFormat.Png : ImageFormat.Jpeg;
                var bytes = await _driver.CaptureScreenshotAsync(false, fmt, _ct);
                var path = ResolveFilePath(await BuildFileNameAsync(s.File, fmt == ImageFormat.Png ? ".png" : ".jpg", ""), folder);
                await File.WriteAllBytesAsync(path, bytes, _ct);
                _host.Log(LogLevel.Info, $"Screenshot saved to {path}");
                break;
            }
            default:
                _host.Log(LogLevel.Warning, $"SAVEAS TYPE={s.Type} is not implemented; skipped");
                break;
        }
    }

    private async Task ExecScreenshotAsync(ScreenshotCommand s)
    {
        var folder = ResolveFolder(s.Folder);
        var bytes = await _driver.CaptureScreenshotAsync(s.Type == "PAGE", ImageFormat.Png, _ct);
        var path = ResolveFilePath(await BuildFileNameAsync(s.File, ".png", ""), folder);
        await File.WriteAllBytesAsync(path, bytes, _ct);
        _host.Log(LogLevel.Info, $"Screenshot saved to {path}");
    }

    // ---- STOPWATCH / SEARCH ----------------------------------------------------------------------------------

    private void ExecStopwatch(StopwatchCommand s)
    {
        var now = _clock.Now;
        var id = s.Id is null ? null : R(s.Id);
        switch (s.Action)
        {
            case StopwatchAction.Label:
                var label = R(s.Label);
                _state.StopwatchTime = (now - _macroStart).TotalSeconds;
                _host.Log(LogLevel.Info, $"Stopwatch label {label}: {_state.StopwatchTime:0.000}s since macro start");
                return;
            case StopwatchAction.Start:
                _stopwatches[id!] = now;
                return;
            case StopwatchAction.Stop:
                if (!_stopwatches.Remove(id!, out var started)) throw new MacroRuntimeException($"STOPWATCH ID={id} was not started");
                RecordStopwatch(id!, now - started);
                return;
            case StopwatchAction.Toggle:
                if (_stopwatches.Remove(id!, out var st)) RecordStopwatch(id!, now - st);
                else _stopwatches[id!] = now;
                return;
        }
    }

    private void RecordStopwatch(string id, TimeSpan elapsed)
    {
        _state.StopwatchTime = elapsed.TotalSeconds;
        _host.Log(LogLevel.Info, $"Stopwatch {id}: {elapsed.TotalSeconds:0.000}s");
    }

    private async Task ExecSearchAsync(SearchCommand s)
    {
        var source = R(s.Source);
        var ignoreCase = s.IgnoreCase is not null && R(s.IgnoreCase).Equals("YES", StringComparison.OrdinalIgnoreCase);
        var html = ScriptBuilder.ParseResult<EvalResult>(await _driver.ExecuteScriptAsync(ScriptBuilder.PageHtmlScript, _ct))?.Value ?? "";
        string pattern;
        if (source.StartsWith("TXT:", StringComparison.OrdinalIgnoreCase)) pattern = Regex.Escape(source[4..]).Replace("\\*", ".*?");
        else if (source.StartsWith("REGEXP:", StringComparison.OrdinalIgnoreCase)) pattern = source[7..];
        else throw new MacroRuntimeException("SEARCH: SOURCE= must start with TXT: or REGEXP:");
        Match m;
        try
        {
            m = Regex.Match(html, pattern, (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.Singleline, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException ex) { throw new MacroRuntimeException("SEARCH: invalid regular expression: " + ex.Message); }
        if (!m.Success) throw new MacroRuntimeException($"SEARCH: pattern not found in page source: {source}");
        if (s.Extract is not null)
        {
            var template = R(s.Extract);
            AddExtract(Regex.Replace(template, @"\$(\d+)", g =>
            {
                var i = int.Parse(g.Groups[1].Value, CultureInfo.InvariantCulture);
                return i < m.Groups.Count ? m.Groups[i].Value : "";
            }));
        }
    }
}
