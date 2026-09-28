using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using WebMacros.App.Browser;
using WebMacros.Engine.Recording;
using WebMacros.Engine.Runtime;
using WebMacros.Engine.Syntax;

namespace WebMacros.App;

public partial class MainWindow : Window
{
    private const int MaxLogEntries = 5000;
    private const string NewMacroTemplate = "VERSION BUILD=1000\r\nTAB T=1\r\nURL GOTO=https://example.com/\r\n";

    private readonly MacroLibrary _library = new();
    private readonly WpfMacroHost _host;
    private WebView2BrowserDriver? _driver;
    private MacroInterpreter? _interpreter;
    private CancellationTokenSource? _playCts;
    private MacroRecorder? _recorder;
    private string? _currentPath;
    private bool _dirty;
    private bool _suppressDirty;
    private bool _suppressListSelection;

    public MainWindow()
    {
        InitializeComponent();
        _host = new WpfMacroHost(this);
    }

    private bool IsPlaying => _playCts is not null;
    private bool IsRecording => _recorder is not null;

    // ---- startup -------------------------------------------------------------------------------------------

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _library.EnsureCreated();
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Error, "Cannot create macro folders: " + ex.Message);
        }
        RefreshMacroList();
        SetEditorText(NewMacroTemplate, null);

        var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebMacros", "WebView2");
        _driver = new WebView2BrowserDriver(BrowserHost, userData);
        _driver.ActiveTabChanged += OnActiveTabChanged;
        _driver.WebMessageReceived += OnWebMessage;
        _driver.NavigationCompleted += tab => { if (tab == _driver.ActiveTab) UpdateNavButtons(); };
        TabStrip.ItemsSource = _driver.Tabs;
        try
        {
            await _driver.InitializeAsync("https://example.com/");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "The Microsoft Edge WebView2 Runtime is not installed.\n\nIt ships with Windows 11; on Windows 10 install it from https://developer.microsoft.com/microsoft-edge/webview2/",
                "WebMacros", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }
        _interpreter = new MacroInterpreter(_driver, _host, new InterpreterOptions
        {
            MacroFolder = _library.MacrosFolder,
            DataSourceFolder = _library.DataSourcesFolder,
            DownloadFolder = _library.DownloadsFolder,
            Version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        });
        AddLog(LogLevel.Info, $"Macros folder: {_library.MacrosFolder}");
        StatusText.Text = "Ready";
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscardChanges()) { e.Cancel = true; return; }
        _playCts?.Cancel();
    }

    // ---- browser chrome ------------------------------------------------------------------------------------

    private void OnActiveTabChanged(BrowserTab tab)
    {
        _suppressListSelection = true;
        TabStrip.SelectedItem = tab;
        _suppressListSelection = false;
        AddressBox.Text = tab.Url;
        tab.PropertyChanged -= OnTabPropertyChanged;
        tab.PropertyChanged += OnTabPropertyChanged;
        UpdateNavButtons();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not BrowserTab tab || tab != _driver?.ActiveTab) return;
        if (e.PropertyName == nameof(BrowserTab.Url) && !AddressBox.IsKeyboardFocusWithin) AddressBox.Text = tab.Url;
        if (e.PropertyName == nameof(BrowserTab.Title)) Title = $"WebMacros – {tab.Title}";
    }

    private void UpdateNavButtons()
    {
        var tab = _driver?.ActiveTab;
        BackButton.IsEnabled = tab is { IsInitialized: true } && tab.Core.CanGoBack;
        ForwardButton.IsEnabled = tab is { IsInitialized: true } && tab.Core.CanGoForward;
    }

    private void NavigateFromAddressBar()
    {
        var tab = _driver?.ActiveTab;
        if (tab is not { IsInitialized: true }) return;
        string url;
        try { url = MacroInterpreter.NormalizeUrl(AddressBox.Text); }
        catch (MacroRuntimeException) { return; }
        try
        {
            tab.Core.Navigate(url);
            _recorder?.AddNavigation(url);
            SyncRecorderToEditor();
        }
        catch (ArgumentException ex)
        {
            AddLog(LogLevel.Error, $"Invalid URL: {ex.Message}");
        }
    }

    private void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) NavigateFromAddressBar();
    }

    private void OnGo(object sender, RoutedEventArgs e) => NavigateFromAddressBar();

    private void OnBack(object sender, RoutedEventArgs e)
    {
        var tab = _driver?.ActiveTab;
        if (tab is { IsInitialized: true } && tab.Core.CanGoBack)
        {
            tab.Core.GoBack();
            _recorder?.AddBack();
            SyncRecorderToEditor();
        }
    }

    private void OnForward(object sender, RoutedEventArgs e)
    {
        var tab = _driver?.ActiveTab;
        if (tab is { IsInitialized: true } && tab.Core.CanGoForward) tab.Core.GoForward();
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        var tab = _driver?.ActiveTab;
        if (tab is not { IsInitialized: true }) return;
        tab.Core.Reload();
        _recorder?.AddRefresh();
        SyncRecorderToEditor();
    }

    private async void OnNewTab(object sender, RoutedEventArgs e)
    {
        if (_driver is null || IsPlaying) return;
        await _driver.CreateTabAsync("about:blank", activate: true);
        if (_recorder is not null)
        {
            _recorder.AddTabOpen();
            _recorder.AddTabSwitch(_driver.CurrentTabIndex + 1);
            SyncRecorderToEditor();
        }
    }

    private void OnCloseTab(object sender, RoutedEventArgs e)
    {
        if (_driver is null || IsPlaying || _driver.TabCount <= 1) return;
        _driver.CloseTab(_driver.CurrentTabIndex);
        if (_recorder is not null) { _recorder.AddTabClose(); SyncRecorderToEditor(); }
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressListSelection || _driver is null || TabStrip.SelectedIndex < 0) return;
        if (TabStrip.SelectedIndex == _driver.CurrentTabIndex) return;
        if (IsPlaying) { OnActiveTabChanged(_driver.ActiveTab!); return; }
        _driver.Activate(TabStrip.SelectedIndex);
        if (_recorder is not null) { _recorder.AddTabSwitch(_driver.CurrentTabIndex + 1); SyncRecorderToEditor(); }
    }

    // ---- macro list / editor -------------------------------------------------------------------------------

    private void RefreshMacroList()
    {
        MacroList.Items.Clear();
        foreach (var path in _library.ListMacros())
        {
            var rel = Path.GetRelativePath(_library.MacrosFolder, path);
            MacroList.Items.Add(new ListBoxItem { Content = rel, Tag = path, ToolTip = path });
        }
    }

    private void OnRefreshList(object sender, RoutedEventArgs e) => RefreshMacroList();

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", _library.Root) { UseShellExecute = true }); }
        catch (Exception ex) { AddLog(LogLevel.Error, ex.Message); }
    }

    private void OnMacroSelected(object sender, SelectionChangedEventArgs e)
    {
        // Load on single click as well as double click (like the iMacros sidebar).
        if (MacroList.SelectedItem is ListBoxItem { Tag: string path } && path != _currentPath && !IsPlaying && !IsRecording)
            LoadMacro(path);
    }

    private void OnMacroDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MacroList.SelectedItem is ListBoxItem { Tag: string path } && !IsRecording)
        {
            if (path != _currentPath) LoadMacro(path);
            if (!IsPlaying) OnPlay(sender, e);
        }
    }

    private void LoadMacro(string path)
    {
        if (!ConfirmDiscardChanges()) return;
        try
        {
            SetEditorText(File.ReadAllText(path), path);
            ValidateEditor();
        }
        catch (IOException ex)
        {
            AddLog(LogLevel.Error, $"Cannot open {path}: {ex.Message}");
        }
    }

    private void SetEditorText(string text, string? path)
    {
        _suppressDirty = true;
        Editor.Text = text;
        _suppressDirty = false;
        _currentPath = path;
        _dirty = false;
        UpdateEditorTitle();
    }

    private void UpdateEditorTitle() =>
        EditorTitle.Text = (_currentPath is null ? "Untitled.iim" : Path.GetFileName(_currentPath)) + (_dirty ? " *" : "");

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressDirty) return;
        if (!_dirty) { _dirty = true; UpdateEditorTitle(); }
    }

    private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
    {
        var line = Editor.GetLineIndexFromCharacterIndex(Editor.CaretIndex);
        CaretInfo.Text = line >= 0 ? $"Ln {line + 1}" : "";
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show(this, "The current macro has unsaved changes. Save them?", "WebMacros",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return Save(saveAs: false);
        return true;
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (IsPlaying || IsRecording || !ConfirmDiscardChanges()) return;
        SetEditorText(NewMacroTemplate, null);
        MacroList.SelectedItem = null;
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save(saveAs: false);
    private void OnSaveAs(object sender, RoutedEventArgs e) => Save(saveAs: true);
    private void OnSaveCommand(object sender, ExecutedRoutedEventArgs e) => Save(saveAs: false);

    private bool Save(bool saveAs)
    {
        var path = _currentPath;
        if (path is null || saveAs)
        {
            var suggested = path is null ? "Macro-" + DateTime.Now.ToString("yyyyMMdd-HHmm") : Path.GetFileNameWithoutExtension(path) + "-copy";
            var name = InputDialog.Ask(this, "Save macro", $"Macro name (saved in {_library.MacrosFolder}):", suggested);
            if (string.IsNullOrWhiteSpace(name)) return false;
            path = _library.PathFor(name.Trim());
            if (File.Exists(path) && MessageBox.Show(this, $"{Path.GetFileName(path)} exists. Overwrite?", "WebMacros",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return false;
        }
        try
        {
            File.WriteAllText(path, Editor.Text, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog(LogLevel.Error, $"Save failed: {ex.Message}");
            return false;
        }
        _currentPath = path;
        _dirty = false;
        UpdateEditorTitle();
        RefreshMacroList();
        foreach (ListBoxItem item in MacroList.Items)
            if ((string)item.Tag == path) { MacroList.SelectedItem = item; break; }
        AddLog(LogLevel.Info, $"Saved {path}");
        ValidateEditor();
        return true;
    }

    private bool ValidateEditor()
    {
        var errors = MacroParser.Validate(Editor.Text);
        foreach (var err in errors) AddLog(LogLevel.Error, err.Message);
        return errors.Count == 0;
    }

    // ---- play / record -------------------------------------------------------------------------------------

    private void OnPlayCommand(object sender, ExecutedRoutedEventArgs e) => OnPlay(sender, e);
    private async void OnPlay(object sender, RoutedEventArgs e) => await PlayAsync(1);

    private async void OnPlayLoop(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(LoopCountBox.Text, out var n) || n < 1)
        {
            MessageBox.Show(this, "Max loops must be a positive number.", "WebMacros");
            return;
        }
        await PlayAsync(n);
    }

    private async Task PlayAsync(int loops)
    {
        if (_interpreter is null || IsPlaying || IsRecording) return;
        if (!ValidateEditor()) { StatusText.Text = "Macro has syntax errors"; return; }

        _playCts = new CancellationTokenSource();
        SetUiPlaying(true);
        var name = _currentPath is null ? "Untitled" : Path.GetFileName(_currentPath);
        AddLog(LogLevel.Info, loops > 1 ? $"▶ Playing {name} ({loops} loops)" : $"▶ Playing {name}");
        StatusText.Text = $"Playing {name}…";
        try
        {
            var result = await _interpreter.PlayAsync(Editor.Text, loops, _playCts.Token);
            StatusText.Text = result.ToString();
            if (result.Status == PlayStatus.Failed && result.ErrorLine is int line)
            {
                HighlightLine(line);
                MessageBox.Show(this, $"Line {line}: {result.ErrorMessage}", "WebMacros – macro error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _playCts.Dispose();
            _playCts = null;
            SetUiPlaying(false);
        }
    }

    private void SetUiPlaying(bool playing)
    {
        PlayButton.IsEnabled = !playing;
        PlayLoopButton.IsEnabled = !playing;
        RecordButton.IsEnabled = !playing;
        NewButton.IsEnabled = !playing;
        StopButton.IsEnabled = playing || IsRecording;
        Editor.IsReadOnly = playing;
        MacroList.IsEnabled = !playing;
        if (!playing) { CurrentLineText.Text = ""; ResumeButton.IsEnabled = false; }
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        if (IsPlaying)
        {
            _playCts?.Cancel();
            return;
        }
        if (IsRecording) _ = StopRecordingAsync();
    }

    private void OnResume(object sender, RoutedEventArgs e) => _host.Resume();

    public void SetPaused(bool paused)
    {
        ResumeButton.IsEnabled = paused;
        StatusText.Text = paused ? "Paused – press Resume" : "Playing…";
    }

    private async void OnRecord(object sender, RoutedEventArgs e)
    {
        if (_driver is null || IsPlaying) return;
        if (IsRecording) { await StopRecordingAsync(); return; }
        if (!ConfirmDiscardChanges()) return;

        _recorder = new MacroRecorder();
        _recorder.Start(_driver.CurrentUrl, typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        SetEditorText(_recorder.GetMacroText(), null);
        _dirty = true;
        UpdateEditorTitle();
        await _driver.StartRecordingAsync(RecorderScript.Build());
        RecordButton.Content = "■ Stop recording";
        StopButton.IsEnabled = true;
        PlayButton.IsEnabled = PlayLoopButton.IsEnabled = false;
        Editor.IsReadOnly = true;
        StatusText.Text = "Recording… interact with the page; clicks, typing and selections become TAG lines";
        AddLog(LogLevel.Info, "● Recording started");
    }

    private async Task StopRecordingAsync()
    {
        if (_driver is null || _recorder is null) return;
        await _driver.StopRecordingAsync(RecorderScript.StopScript);
        SyncRecorderToEditor();
        _recorder = null;
        RecordButton.Content = "● Record";
        StopButton.IsEnabled = false;
        PlayButton.IsEnabled = PlayLoopButton.IsEnabled = true;
        Editor.IsReadOnly = false;
        StatusText.Text = "Recording stopped – press Save to keep the macro";
        AddLog(LogLevel.Info, "Recording stopped");
    }

    private void OnWebMessage(BrowserTab tab, string message)
    {
        if (_recorder is null) return;
        var action = RecordedAction.Parse(message);
        if (action is null) return;
        if (_driver is not null && tab != _driver.ActiveTab) return;
        var line = _recorder.Add(action);
        if (line is not null) SyncRecorderToEditor();
    }

    private void SyncRecorderToEditor()
    {
        if (_recorder is null) return;
        _suppressDirty = true;
        Editor.Text = _recorder.GetMacroText();
        _suppressDirty = false;
        Editor.CaretIndex = Editor.Text.Length;
        Editor.ScrollToEnd();
    }

    // ---- current line / log --------------------------------------------------------------------------------

    public void ShowCurrentLine(int lineNumber, string text, int loop)
    {
        CurrentLineText.Text = $"Loop {loop} · Line {lineNumber}: {text}";
        HighlightLine(lineNumber);
    }

    private void HighlightLine(int lineNumber)
    {
        var index = lineNumber - 1;
        if (index < 0 || index >= Editor.LineCount) return;
        var start = Editor.GetCharacterIndexFromLineIndex(index);
        var length = Editor.GetLineLength(index);
        var lineText = Editor.GetLineText(index);
        var trimmed = lineText.TrimEnd('\r', '\n').Length;
        Editor.Select(start, Math.Min(length, trimmed));
        Editor.ScrollToLine(index);
    }

    public void AddLog(LogLevel level, string message)
    {
        var brush = level switch
        {
            LogLevel.Error => Brushes.Firebrick,
            LogLevel.Warning => Brushes.DarkOrange,
            LogLevel.Extract => Brushes.DarkGreen,
            _ => Brushes.Black,
        };
        var prefix = level switch { LogLevel.Extract => "EXTRACT ", LogLevel.Error => "ERROR ", LogLevel.Warning => "WARN ", _ => "" };
        var item = new TextBlock
        {
            Text = $"{DateTime.Now:HH:mm:ss} {prefix}{message}",
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap,
        };
        LogList.Items.Add(item);
        while (LogList.Items.Count > MaxLogEntries) LogList.Items.RemoveAt(0);
        LogList.ScrollIntoView(item);
    }

    private void OnClearLog(object sender, RoutedEventArgs e) => LogList.Items.Clear();

    private void OnCopyLog(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        foreach (var item in LogList.Items.OfType<TextBlock>()) sb.AppendLine(item.Text);
        try { Clipboard.SetText(sb.ToString()); } catch (Exception ex) { AddLog(LogLevel.Error, "Clipboard: " + ex.Message); }
    }
}
