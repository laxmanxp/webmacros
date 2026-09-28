using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Jint;
using Jint.Runtime;
using WebMacros.Engine.Runtime;
using WebMacros.Engine.Scripting;
using WebMacros.Engine.Syntax;

namespace WebMacros.Engine.Scripts;

/// <summary>Return values of <c>iimPlay()</c>.</summary>
public static class IimReturnCodes
{
    public const int Ok = 1;
    /// <summary>The macro failed while running (see iimGetLastError()).</summary>
    public const int MacroError = -1;
    /// <summary>The macro text has a syntax error.</summary>
    public const int SyntaxError = -2;
    /// <summary>The macro file was not found.</summary>
    public const int MacroNotFound = -3;
    /// <summary>The macro was stopped (Stop button).</summary>
    public const int Stopped = -101;
}

/// <summary>Outcome of a .js script run.</summary>
public sealed record ScriptResult
{
    public PlayStatus Status { get; init; }
    /// <summary>1-based line in the script for an uncaught JavaScript error or syntax error.</summary>
    public int? ErrorLine { get; init; }
    public string? ErrorMessage { get; init; }
    public int MacrosPlayed { get; init; }
    public TimeSpan Elapsed { get; init; }
    public bool Success => Status == PlayStatus.Completed;

    public override string ToString() => Status switch
    {
        PlayStatus.Completed => $"Script completed ({MacrosPlayed} macro(s) played) in {Elapsed.TotalSeconds:0.00}s",
        PlayStatus.Stopped => $"Script stopped by user after {MacrosPlayed} macro(s)",
        _ => ErrorLine is int l ? $"Script error on line {l}: {ErrorMessage}" : $"Script error: {ErrorMessage}",
    };
}

/// <summary>
/// Runs iMacros-style JavaScript (.js) macros with Jint. The script runs on its own thread; every call that
/// touches the browser or the UI (iimPlay, iimEval, alert, console.log, ...) is posted to the
/// <see cref="SynchronizationContext"/> that was current when <see cref="RunAsync"/> was called (the WPF UI
/// thread in the app), and the script thread waits for it. Cancelling the token aborts the script, including
/// tight loops (Jint checks the token between statements) and a macro running inside iimPlay.
/// </summary>
public sealed class ScriptRunner
{
    public const string NoData = "#nodata#";

    private readonly MacroInterpreter _interpreter;
    private readonly IBrowserDriver _driver;
    private readonly IMacroHost _host;
    private readonly InterpreterOptions _options;

    public ScriptRunner(MacroInterpreter interpreter, IBrowserDriver driver, IMacroHost? host = null, InterpreterOptions? options = null)
    {
        _interpreter = interpreter;
        _driver = driver;
        _host = host ?? new NullMacroHost();
        _options = options ?? new InterpreterOptions();
    }

    public bool IsRunning { get; private set; }

    /// <summary>Runs a script. <paramref name="scriptFolder"/> is searched first for iimPlay("name") and readCsv().</summary>
    public Task<ScriptResult> RunAsync(string code, CancellationToken ct = default, string? scriptFolder = null)
    {
        if (IsRunning) throw new InvalidOperationException("A script is already running");
        IsRunning = true;
        var session = new Session(this, SynchronizationContext.Current, ct, scriptFolder);
        var tcs = new TaskCompletionSource<ScriptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ScriptResult? result = null;
            Exception? error = null;
            try { result = session.Run(code); }
            catch (Exception ex) { error = ex; }
            IsRunning = false; // before completing the task so awaiting callers can start another run
            if (error is null) tcs.SetResult(result!); else tcs.SetException(error);
        }, 16 * 1024 * 1024) { IsBackground = true, Name = "WebMacros script" };
        thread.Start();
        return tcs.Task;
    }

    /// <summary>Checks the JavaScript syntax without running it. Returns (line, message) of the first error, if any.</summary>
    public static IReadOnlyList<(int Line, string Message)> Validate(string code)
    {
        try
        {
            new Acornima.Parser().ParseScript(code);
            return Array.Empty<(int, string)>();
        }
        catch (Acornima.ParseErrorException ex)
        {
            return new[] { (ex.LineNumber, ex.Description ?? ex.Message) };
        }
    }

    /// <summary>JavaScript defining the public iim* API on top of the __wm* host functions.</summary>
    internal const string Prelude = """
var __wmFmt = function (v) {
  if (typeof v === 'string') return v;
  if (v === undefined) return 'undefined';
  if (typeof v === 'function') return String(v);
  try { var s = JSON.stringify(v); return s === undefined ? String(v) : s; } catch (e) { return String(v); }
};
var __wmJoin = function (args) { var a = []; for (var i = 0; i < args.length; i++) a.push(__wmFmt(args[i])); return a.join(' '); };
var console = {
  log: function () { __wmLog(0, __wmJoin(arguments)); },
  info: function () { __wmLog(0, __wmJoin(arguments)); },
  debug: function () { __wmLog(0, __wmJoin(arguments)); },
  warn: function () { __wmLog(2, __wmJoin(arguments)); },
  error: function () { __wmLog(3, __wmJoin(arguments)); }
};
function iimPlay(macro) { return __wmPlay(macro === undefined || macro === null ? '' : String(macro)); }
function iimSet(name, value) { return __wmSet(String(name), value === undefined || value === null ? '' : String(value)); }
function iimGetLastExtract(n) { return __wmExtract(n === undefined || n === null ? 0 : Number(n)); }
function iimGetExtract(n) { return iimGetLastExtract(n); }
function iimGetLastError() { return __wmLastError(); }
function iimGetErrorText() { return __wmLastError(); }
function iimGetLastErrorCode() { return __wmLastErrorCode(); }
function iimDisplay(message) { __wmDisplay(__wmFmt(message)); return 1; }
function iimExit() { __wmExit(); }
function iimInit() { return 1; }
function iimClose() { return 1; }
function iimEval(js) {
  var r = JSON.parse(__wmEval(String(js)));
  if (!r.ok) throw new Error('iimEval: ' + r.error);
  return r.value;
}
function alert(message) { __wmAlert(message === undefined ? '' : __wmFmt(message)); }
function confirm(message) { return __wmConfirm(message === undefined ? '' : __wmFmt(message)); }
function prompt(message, value) {
  return JSON.parse(__wmPrompt(message === undefined ? '' : __wmFmt(message), value === undefined || value === null ? '' : String(value)));
}
function readCsv(file, delimiter) {
  var r = JSON.parse(__wmReadCsv(String(file), delimiter === undefined || delimiter === null ? '' : String(delimiter)));
  if (!r.ok) throw new Error(r.error);
  return r.rows;
}
function readTextFile(file) {
  var r = JSON.parse(__wmReadText(String(file)));
  if (!r.ok) throw new Error(r.error);
  return r.text;
}
""";

    private sealed class ScriptExitException() : Exception("iimExit()");
    private sealed class ScriptAbortException() : Exception("Script stopped");

    private sealed class Session
    {
        private readonly ScriptRunner _r;
        private readonly SynchronizationContext? _ctx;
        private readonly CancellationToken _ct;
        private readonly string? _scriptFolder;
        private readonly List<KeyValuePair<string, string>> _pendingVars = new();
        private IReadOnlyList<string> _lastExtract = Array.Empty<string>();
        private string _lastError = "";
        private int _lastErrorCode = IimReturnCodes.Ok;
        private int _played;

        public Session(ScriptRunner runner, SynchronizationContext? ctx, CancellationToken ct, string? scriptFolder)
        {
            _r = runner;
            _ctx = ctx;
            _ct = ct;
            _scriptFolder = scriptFolder;
        }

        public ScriptResult Run(string code)
        {
            var sw = Stopwatch.StartNew();
            ScriptResult Done(PlayStatus status, int? line = null, string? message = null) => new()
            {
                Status = status, ErrorLine = line, ErrorMessage = message, MacrosPlayed = _played, Elapsed = sw.Elapsed,
            };

            var engine = new Jint.Engine(o =>
            {
                o.CancellationToken(_ct);
                o.LimitRecursion(1000);
            });
            engine.SetValue("__wmPlay", new Func<string, int>(Play));
            engine.SetValue("__wmSet", new Func<string, string, int>(SetVar));
            engine.SetValue("__wmExtract", new Func<double, string>(GetExtract));
            engine.SetValue("__wmLastError", new Func<string>(() => _lastError));
            engine.SetValue("__wmLastErrorCode", new Func<int>(() => _lastErrorCode));
            engine.SetValue("__wmDisplay", new Action<string>(Display));
            engine.SetValue("__wmExit", new Action(() => throw new ScriptExitException()));
            engine.SetValue("__wmEval", new Func<string, string>(EvalInPage));
            engine.SetValue("__wmLog", new Action<int, string>(Log));
            engine.SetValue("__wmAlert", new Action<string>(m => OnHost(async () => { await _r._host.ShowMessageAsync(m, _ct); return 0; })));
            engine.SetValue("__wmConfirm", new Func<string, bool>(m => OnHost(() => _r._host.ConfirmAsync(m, _ct))));
            engine.SetValue("__wmPrompt", new Func<string, string, string>((m, d) =>
                JsonSerializer.Serialize(OnHost(() => _r._host.PromptAsync(m, d, _ct)))));
            engine.SetValue("__wmReadCsv", new Func<string, string, string>(ReadCsv));
            engine.SetValue("__wmReadText", new Func<string, string>(ReadText));

            try
            {
                engine.Execute(Prelude, "webmacros-prelude");
                engine.Execute(code, "script");
                return Done(PlayStatus.Completed);
            }
            catch (ScriptExitException)
            {
                PostLog(LogLevel.Info, "iimExit() called; script ended");
                return Done(PlayStatus.Completed);
            }
            catch (Exception ex) when (ex is ScriptAbortException or ExecutionCanceledException or OperationCanceledException)
            {
                PostLog(LogLevel.Warning, "Script stopped");
                return Done(PlayStatus.Stopped);
            }
            catch (JavaScriptException ex)
            {
                var line = ex.Location.Start.Line;
                var msg = ex.Error.IsObject() && ex.Error.AsObject().Get("message").IsString() ? ex.Error.AsObject().Get("message").AsString() : ex.Message;
                if (ex.Error.IsObject() && ex.Error.AsObject().Get("name").IsString())
                    msg = ex.Error.AsObject().Get("name").AsString() + ": " + msg;
                return Fail(line > 0 ? line : null, msg);
            }
            catch (Acornima.ParseErrorException ex)
            {
                return Fail(ex.LineNumber, "SyntaxError: " + (ex.Description ?? ex.Message));
            }
            catch (Exception ex) when (ex is RecursionDepthOverflowException or MemoryLimitExceededException or StatementsCountOverflowException or JintException)
            {
                return Fail(null, ex.Message);
            }

            ScriptResult Fail(int? line, string message)
            {
                PostLog(LogLevel.Error, line is int l ? $"Script line {l}: {message}" : $"Script error: {message}");
                return Done(PlayStatus.Failed, line, message);
            }
        }

        /// <summary>Runs <paramref name="work"/> on the UI context (or inline without one) and waits for it.</summary>
        /// <remarks>With <paramref name="waitForCompletion"/> the wait ignores cancellation, for work that observes the token
        /// itself (macro playback), so a Stop lets the macro wind down and report Stopped before the script aborts.</remarks>
        private T OnHost<T>(Func<Task<T>> work, bool waitForCompletion = false)
        {
            if (_ct.IsCancellationRequested) throw new ScriptAbortException();
            Task<T> task;
            if (_ctx is null)
            {
                task = work();
            }
            else
            {
                var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ctx.Post(async _ =>
                {
                    try { tcs.TrySetResult(await work().ConfigureAwait(true)); }
                    catch (Exception e) { tcs.TrySetException(e); }
                }, null);
                task = tcs.Task;
            }
            try
            {
                if (waitForCompletion) task.Wait(); else task.Wait(_ct);
            }
            catch (OperationCanceledException)
            {
                throw new ScriptAbortException();
            }
            catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
            {
                throw new ScriptAbortException();
            }
            catch (AggregateException ae) when (ae.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ae.InnerException).Throw();
            }
            return task.Result;
        }

        private void OnHost(Action action) => OnHost(() => { action(); return Task.FromResult(0); });

        /// <summary>Logs without waiting and without honouring cancellation (used for the final outcome message).</summary>
        private void PostLog(LogLevel level, string message)
        {
            if (_ctx is null) { _r._host.Log(level, message); return; }
            _ctx.Post(_ => _r._host.Log(level, message), null);
        }

        private void Log(int level, string message)
        {
            var l = Enum.IsDefined(typeof(LogLevel), level) ? (LogLevel)level : LogLevel.Info;
            OnHost(() => _r._host.Log(l, message));
        }

        private void Display(string message) => OnHost(() =>
        {
            _r._host.ShowStatus(message);
            _r._host.Log(LogLevel.Info, "iimDisplay: " + message);
        });

        private int SetVar(string name, string value)
        {
            if (name.StartsWith("-var_", StringComparison.OrdinalIgnoreCase)) name = name[5..]; // iMacros 6 syntax
            if (name.Length == 0) throw new ArgumentException("iimSet: variable name is empty");
            _pendingVars.Add(new(name, value));
            return IimReturnCodes.Ok;
        }

        private string GetExtract(double n)
        {
            if (double.IsNaN(n) || n <= 0) return string.Join(MacroState.ExtractSeparator, _lastExtract);
            var i = (int)n;
            return i <= _lastExtract.Count ? _lastExtract[i - 1] : NoData;
        }

        private int Error(int code, string message)
        {
            _lastErrorCode = code;
            _lastError = message;
            _lastExtract = Array.Empty<string>();
            Log(3, "iimPlay: " + message);
            return code;
        }

        private string? FindFile(string name, IEnumerable<string> folders, string? defaultExtension)
        {
            var names = new List<string> { name };
            if (defaultExtension is not null && !name.EndsWith(defaultExtension, StringComparison.OrdinalIgnoreCase)) names.Add(name + defaultExtension);
            foreach (var n in names)
            {
                if (Path.IsPathRooted(n)) { if (File.Exists(n)) return n; continue; }
                foreach (var folder in folders)
                {
                    var p = Path.Combine(folder, n);
                    if (File.Exists(p)) return p;
                }
            }
            return null;
        }

        private IEnumerable<string> MacroFolders()
        {
            if (_scriptFolder is not null) yield return _scriptFolder;
            yield return _r._options.MacroFolder;
        }

        private IEnumerable<string> DataFolders()
        {
            yield return _r._options.DataSourceFolder;
            foreach (var f in MacroFolders()) yield return f;
        }

        private int Play(string macro)
        {
            var vars = _pendingVars.ToArray();
            _pendingVars.Clear(); // iimSet values apply to the next iimPlay only
            string text, label;
            if (macro.StartsWith("CODE:", StringComparison.OrdinalIgnoreCase))
            {
                text = macro[5..];
                label = "CODE";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(macro)) return Error(IimReturnCodes.MacroNotFound, "no macro name given");
                var path = FindFile(macro.Trim(), MacroFolders(), ".iim");
                if (path is null) return Error(IimReturnCodes.MacroNotFound, $"macro not found: {macro}");
                try { text = File.ReadAllText(path, Encoding.UTF8); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return Error(IimReturnCodes.MacroNotFound, $"cannot read {path}: {ex.Message}");
                }
                label = Path.GetFileName(path);
            }

            ParsedMacro parsed;
            try { parsed = MacroParser.Parse(text); }
            catch (MacroSyntaxException ex) { return Error(IimReturnCodes.SyntaxError, $"{label}: {ex.Message}"); }

            Log(0, $"iimPlay {label}");
            var run = new MacroRunOptions { Variables = vars, ShowExtractPopup = false };
            var result = OnHost(() => _r._interpreter.PlayAsync(parsed, 1, _ct, run), waitForCompletion: true);
            _played++;
            _lastExtract = _r._interpreter.State?.ExtractValues.ToArray() ?? Array.Empty<string>();
            switch (result.Status)
            {
                case PlayStatus.Completed:
                    _lastError = "";
                    _lastErrorCode = IimReturnCodes.Ok;
                    return IimReturnCodes.Ok;
                case PlayStatus.Stopped:
                    if (_ct.IsCancellationRequested) throw new ScriptAbortException();
                    _lastErrorCode = IimReturnCodes.Stopped;
                    _lastError = "Macro stopped";
                    return IimReturnCodes.Stopped;
                default:
                    _lastErrorCode = IimReturnCodes.MacroError;
                    _lastError = result.ErrorLine is int line ? $"{label} line {line}: {result.ErrorMessage}" : $"{label}: {result.ErrorMessage}";
                    return IimReturnCodes.MacroError;
            }
        }

        private string EvalInPage(string js)
        {
            var res = OnHost(async () =>
            {
                var raw = await _r._driver.ExecuteScriptAsync(ScriptBuilder.BuildEvalScript(js), _ct).ConfigureAwait(true);
                return ScriptBuilder.ParseResult<EvalResult>(raw);
            });
            if (res is null) return JsonSerializer.Serialize(new { ok = false, error = "no result from page" });
            return res.Ok
                ? JsonSerializer.Serialize(new { ok = true, value = res.Value ?? "" })
                : JsonSerializer.Serialize(new { ok = false, error = res.Error ?? "error" });
        }

        private string ReadCsv(string file, string delimiter)
        {
            var path = FindFile(file, DataFolders(), null);
            if (path is null) return JsonSerializer.Serialize(new { ok = false, error = $"readCsv: file not found: {file}" });
            var d = delimiter switch { "" => ',', "\\t" or "TAB" or "tab" => '\t', _ when delimiter.Length == 1 => delimiter[0], _ => '\0' };
            if (d == '\0') return JsonSerializer.Serialize(new { ok = false, error = "readCsv: delimiter must be one character" });
            try
            {
                var rows = Csv.Parse(File.ReadAllText(path, Encoding.UTF8), d);
                return JsonSerializer.Serialize(new { ok = true, rows });
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                return JsonSerializer.Serialize(new { ok = false, error = $"readCsv: {path}: {ex.Message}" });
            }
        }

        private string ReadText(string file)
        {
            var path = FindFile(file, DataFolders(), null);
            if (path is null) return JsonSerializer.Serialize(new { ok = false, error = $"readTextFile: file not found: {file}" });
            try { return JsonSerializer.Serialize(new { ok = true, text = File.ReadAllText(path, Encoding.UTF8) }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return JsonSerializer.Serialize(new { ok = false, error = $"readTextFile: {ex.Message}" });
            }
        }
    }
}
