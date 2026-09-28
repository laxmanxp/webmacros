# WebMacros

WebMacros is a Windows desktop browser-automation tool in the style of **iMacros**. It has a built-in
Chromium browser (Microsoft Edge **WebView2**) and a macro recorder, and it plays back macros written in an
iMacros-compatible `.iim` language: fill forms, click, extract data to CSV, loop over CSV datasources,
take screenshots, answer JavaScript dialogs and more.

```
VERSION BUILD=1000
TAB T=1
URL GOTO=https://html.duckduckgo.com/html/
TAG POS=1 TYPE=INPUT:TEXT ATTR=NAME:q CONTENT=webview2<SP>automation<ENTER>
TAG POS=1 TYPE=A ATTR=CLASS:result__a EXTRACT=TXT
SAVEAS TYPE=EXTRACT FOLDER=* FILE=results.csv
```

## Contents

- [Requirements](#requirements)
- [Build and run](#build-and-run)
- [Using the app](#using-the-app)
- [Project layout](#project-layout)
- [Language reference](#language-reference)
- [Not implemented / differences from iMacros](#not-implemented--differences-from-imacros)

## Requirements

- Windows 10 or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Microsoft Edge **WebView2 Runtime**. It ships with Windows 11. On Windows 10, install the
  [Evergreen runtime](https://developer.microsoft.com/microsoft-edge/webview2/) if it is missing.

The engine and its tests are plain `net10.0` and also build and run on Linux and macOS. The WPF app project
builds everywhere (`EnableWindowsTargeting`), but it only runs on Windows.

## Build and run

```powershell
git clone https://github.com/laxmanxp/webmacros.git
cd webmacros
dotnet build
dotnet test
dotnet run --project src/WebMacros.App
```

On first start, WebMacros creates these folders:

| Folder | Purpose |
|---|---|
| `%USERPROFILE%\Documents\WebMacros\Macros` | Your `*.iim` macros. Four sample macros are added on first run. |
| `%USERPROFILE%\Documents\WebMacros\Datasources` | CSV files for `!DATASOURCE`. `customers.csv` is added as a sample. |
| `%USERPROFILE%\Documents\WebMacros\Downloads` | Default target for `SAVEAS` / `SCREENSHOT` when `FOLDER=*` is used. |
| `%LOCALAPPDATA%\WebMacros\WebView2` | Browser profile (cookies, cache). |

### Sample macros

| File | What it shows |
|---|---|
| `01-DuckDuckGo-Search-Extract.iim` | Searches html.duckduckgo.com, extracts the first result titles and saves them to CSV (`!ERRORIGNORE`, `#EANF#`). |
| `02-Fill-Form-From-CSV.iim` | Fills https://httpbin.org/forms/post once for each row of `customers.csv` (text, tel, email, radio, checkbox, time, textarea, button). Use **Play Loop** with Max = 4. |
| `03-Loop-Visit-Pages.iim` | Visits a list of pages chosen with `EVAL` and `!LOOP`, times each load with `STOPWATCH`, and writes url/title/time/`!NOW` to CSV. Use **Play Loop** with Max = 3. |
| `04-Dialogs-Eval-Prompt-Demo.iim` | `PROMPT`, `EVAL`, and `ONDIALOG` answering `alert` / `confirm` / `prompt` automatically. |

## Using the app

- **Browser**: address bar, back/forward/reload, and tabs (`+` / `✕`). Links that open a new window open in a new tab.
- **Side panel**: the macros in the Macros folder. A single click opens a macro in the editor, and a double click also plays it.
  - **Play** (F5) runs the macro in the editor once. It plays the editor text, so you don't need to save first.
  - **Play Loop** runs it *Max* times. `!LOOP` counts 1, 2, 3, ...
  - **Record** captures your clicks, typing, select/checkbox changes, Enter-submits, address-bar navigations, back/refresh and tab switches as macro lines in a new macro.
  - **Stop** stops playback or recording. **Resume** continues after `PAUSE`.
  - **New**, **Save** (Ctrl+S) and **Save as…** manage macro files. **Folder** opens the WebMacros folder in Explorer.
- **Editor**: a plain monospace editor. During playback, the line that is running is highlighted, and the status bar shows `Loop n · Line m`. If a macro fails, the failing line stays highlighted and an error box shows the line number and message.
- **Log**: shows each loop, extracted values (green), warnings (orange), errors (red), dialogs, stopwatch results and saved files.

## Project layout

```
WebMacros.sln
src/WebMacros.Engine/          net10.0 class library – no UI and no browser dependency
  Syntax/                      Tokenizer, MacroParser, command records
  Scripting/                   FinderRuntime (JS), TagSpecBuilder, ScriptBuilder
  Runtime/                     MacroInterpreter, MacroState (variables), IBrowserDriver, IMacroHost, CSV
  Recording/                   RecorderScript (JS), RecordedAction, MacroRecorder (JS events -> TAG lines)
  Samples/                     Sample macros seeded on first run
src/WebMacros.App/             net10.0-windows WPF app (WebView2)
  Browser/WebView2BrowserDriver.cs   IBrowserDriver on CoreWebView2
tests/WebMacros.Engine.Tests/  xUnit tests. They use a fake IBrowserDriver that runs the generated JS
                               against a small DOM in Jint.
```

All element finding runs in JavaScript that the engine generates (`FinderRuntime` plus a JSON spec). The
same code runs in the real browser, in the recorder (to compute `POS=`), and in the unit tests, so matching
works the same way in all three.

## Language reference

### General syntax

- Each line holds one command: `COMMAND PARAM=value PARAM2=value ...`. Command and parameter names are case-insensitive.
- Whitespace separates parameters. To put spaces in a value, write `<SP>` or put the value in double quotes: `CONTENT="hello world"`. Inside quotes, `\"` and `\\` are escapes, and so are `\n` and `\t`.
- Other escapes in values: `<SP>` (space), `<BR>` (newline), `<TAB>` (tab). In `TAG ... CONTENT=`, `<ENTER>` at the end of the value presses Enter after filling the field, which submits the form.
- Lines starting with `'` (or `//`) are comments. Blank lines are ignored. Comments at the end of a line are **not** supported.
- `{{name}}` inserts the value of a variable. `{{!NOW:format}}` inserts the current date/time.
- WebMacros checks the syntax of the whole macro before it runs. Errors are reported with their line numbers.

### Variables

| Variable | Meaning |
|---|---|
| `!VAR0` … `!VAR9` | General-purpose variables (the default value is empty). |
| user variables | Any name made of letters, digits and `_`, e.g. `SET price 10` then `{{price}}`. Using an undefined variable is an error. |
| `!LOOP` | The current loop number (starts at 1). `SET !LOOP n` is applied only in the first loop and sets the start value; the loop then runs up to *Max*. |
| `!EXTRACT` | Extracted data, with values joined by `[EXTRACT]`. `SET !EXTRACT NULL` clears it, `SET !EXTRACT x` replaces it, `ADD !EXTRACT x` appends to it. It is cleared at the start of each loop. |
| `!EXTRACT_TEST_POPUP` | `YES` (default) shows the extracted data in a dialog at the end of each loop. `NO` turns this off. |
| `!TIMEOUT_STEP` | Seconds that `TAG` / `EVENT` / `FRAME` keep retrying until the element exists. The default is 6. `0` means a single attempt. |
| `!TIMEOUT_PAGE` (alias `!TIMEOUT`) | Seconds to wait for a page load. The default is 60. |
| `!ERRORIGNORE` | With `YES`, a failing line is logged as a warning and the macro continues. `NO` is the default. |
| `!ERRORLOOP` | With `YES`, an error skips to the next loop (Play Loop only). |
| `!DATASOURCE` | CSV file. The path is looked up in the Datasources folder, then in the macro folder, or it can be absolute. |
| `!DATASOURCE_LINE` | The 1-based CSV line used by `!COLn` (usually `{{!LOOP}}`). Reading past the end is an error ("End of datasource"). |
| `!DATASOURCE_COLUMNS` | Read: the number of columns on the current line. It can also be set (legacy). |
| `!DATASOURCE_DELIMITER` | The CSV delimiter (default `,`). |
| `!COL1` … `!COLn` | A column of the current datasource line. |
| `!NOW:format` | The date/time. Tokens: `yyyy` `yy` `mm` (month) `dd` `hh` `nn` (minutes) `ss` `dow` (1 = Sunday) `doy`. Example: `{{!NOW:yyyymmdd_hhnnss}}`. |
| `!STOPWATCHTIME` | Seconds measured by the last `STOPWATCH` stop, in the form `0.000`. |
| `!URLCURRENT` | The URL of the current tab. |
| `!FOLDER_DATASOURCE`, `!FOLDER_DOWNLOAD` | Override the default folders. |

These are accepted but have no effect (you get a warning): `!REPLAYSPEED`, `!ENCRYPTION`, `!FILESTOPWATCH`,
`!WAITPAGECOMPLETE`, `!SINGLESTEP`, `!FILE_PROFILER`, `!POPUP_ALLOWED`, `!USERAGENT`, `!FILELOG`, `!MARKOLDDATA`,
`!DOWNLOAD_PDF`, `!STOPWATCH_HEADER`, `!FOLDER_STOPWATCH`.

### Commands

#### `VERSION BUILD=n`
Information only. Recorded macros start with this line.

#### `TAB OPEN` | `TAB T=n` | `TAB CLOSE` | `TAB CLOSEALLOTHERS`
`TAB OPEN` opens a new tab but does not switch to it. `TAB T=n` switches to tab *n* (1-based). `TAB CLOSE` closes the current tab, and closing the last tab is an error.
```
TAB OPEN
TAB T=2
URL GOTO=https://example.org/
TAB CLOSE
```

#### `URL GOTO=url`
Loads a page and waits until it has finished loading (`!TIMEOUT_PAGE`). A URL without a scheme gets `https://`.
`URL GOTO=javascript:code` runs the code in the page instead.

#### `BACK`, `REFRESH`
Go back one page, or reload. Both wait for the page to load.

#### `WAIT SECONDS=n`
Pauses for *n* seconds. Decimals are allowed.

#### `SET var value` / `SET var EVAL("js")`
Sets a variable. `EVAL("...")` first replaces the `{{variables}}` in the JavaScript, then runs it in the current page
(through `ExecuteScriptAsync`), and stores the result as a string. Calling `MacroError("message")` inside EVAL stops the macro
with that message, even when `!ERRORIGNORE YES` is set.
```
SET !VAR1 "hello world"
SET !VAR2 EVAL("'{{!VAR1}}'.toUpperCase();")
SET n EVAL("var x = {{!LOOP}}; if (x > 5) MacroError('too many'); x * 2;")
```

#### `ADD var value`
Adds numerically when both values are numbers, and otherwise appends the text. `ADD !EXTRACT value` appends an extract value.

#### `TAG`
Finds an element and then clicks it, fills it, or extracts from it.

```
TAG POS=n TYPE=tag[:type] [FORM=...] ATTR=KEY:value[&&KEY:value...] [CONTENT=...|EXTRACT=...]
TAG XPATH="xpath" [CONTENT=...|EXTRACT=...]
TAG SELECTOR="css selector" [POS=n] [CONTENT=...|EXTRACT=...]
```

- `POS=n`: the n-th matching element in document order (default 1). `POS=Rn` / `POS=R-n` counts relative to the element found by the previous `TAG` (the *anchor*): the n-th match after it, or before it for negative values.
- `TYPE=`: the tag name, optionally with the input/button type: `A`, `DIV`, `SELECT`, `TEXTAREA`, `INPUT:TEXT` (an input without a type counts as TEXT), `INPUT:PASSWORD`, `INPUT:CHECKBOX`, `BUTTON:SUBMIT`, `INPUT:*`, or `*` for any tag.
- `ATTR=`: conditions joined with `&&`. All of them must match.
  - `TXT:` matches the visible text (for `<input>` it matches the value).
  - `NAME:`, `ID:`, `CLASS:`, `HREF:`, `VALUE:`, `SRC:`, `ALT:`, `TITLE:`, or **any attribute name** (e.g. `DATA-ID:5`, `PLACEHOLDER:Search`).
  - `CLASS:` matches either the whole class string or a single class name.
  - `HREF:` / `SRC:` match either the written attribute or the absolute URL.
  - `*` is a wildcard: `TXT:Next*`, `HREF:*example.com*`. Matching is case-sensitive and anchored (the whole value must match), and whitespace is collapsed.
  - `ATTR=*` (or no ATTR) matches every element of the TYPE.
- `FORM=NAME:x` / `FORM=ID:x` / `FORM=ACTION:*x*`: the element must be inside a matching form.
- No `CONTENT` and no `EXTRACT` means the element is **clicked** (with focus and mouseover/down/up events, then `click()`).
- `CONTENT=`:
  - text inputs / textarea: sets the value and fires `input` and `change` events. It uses the native value setter, so frameworks such as React should see the change. Add `<ENTER>` at the end to press Enter and submit the form.
  - `SELECT`: `%value`, `$visible text`, or `#index` (1-based). Wildcards are allowed. Join several choices with `:` for multi-selects (`%a:%b`). A value without a prefix is tried as a value first and then as text.
  - checkbox / radio: `YES` / `NO` (also `TRUE` / `FALSE` / `ON` / `OFF`).
  - contenteditable elements: sets the text.
  - `CONTENT=EVENT:MOUSEOVER`, `EVENT:CLICK`, `EVENT:FAIL_IF_FOUND`.
- `EXTRACT=`:
  - `TXT`: the text. For inputs it is the value, and for a select it is the selected option's text.
  - `TXTALL`: all option texts of a select, joined by `[OPTION]`.
  - `HTM`: the outerHTML.
  - `HREF`, `TITLE`, `ALT`, `CHECKED` (YES/NO), or any attribute name.
  - A missing attribute gives `#EANF#`.
- Retries: when the element is missing, `TAG` retries every 250 ms until `!TIMEOUT_STEP` runs out. It then fails with *Element not found*. With `!ERRORIGNORE YES`, a failed `EXTRACT` stores `#EANF#`.
- After a click or content change, WebMacros waits for any page load that the action started.

```
TAG POS=1 TYPE=INPUT:TEXT ATTR=NAME:q CONTENT={{!COL1}}
TAG POS=1 TYPE=SELECT ATTR=ID:country CONTENT=$India
TAG POS=1 TYPE=INPUT:CHECKBOX ATTR=NAME:terms CONTENT=YES
TAG POS=2 TYPE=A ATTR=TXT:Next* EXTRACT=HREF
TAG POS=1 TYPE=TD ATTR=TXT:Price
TAG POS=R1 TYPE=TD ATTR=* EXTRACT=TXT
TAG POS=1 TYPE=BUTTON ATTR=TXT:Submit<SP>order
TAG XPATH="//table[@id='t']//tr[2]/td[3]" EXTRACT=TXT
TAG SELECTOR="#results .item a" POS=3 EXTRACT=TXT
```

#### `EVENT TYPE=... (SELECTOR="css" | XPATH="xpath") [KEY=n] [CHARS="text"]`
Sends DOM events. Without SELECTOR/XPATH, the event goes to the focused element.
Types: `CLICK`, `DBLCLICK`, `MOUSEOVER`, `MOUSEDOWN`, `MOUSEUP`, `MOUSEMOVE`, `KEYPRESS` / `KEYDOWN` / `KEYUP`,
`FOCUS`, `BLUR`, `CHANGE`, `INPUT`, `SUBMIT`. `KEYPRESS CHARS="abc"` types the characters one at a time, with key events,
into inputs. `KEY=13` presses Enter, which submits the form around an input. `KEY=8` is Backspace. `EVENTS` works as an alias.
```
EVENT TYPE=CLICK SELECTOR="#menu > li:nth-child(2)"
EVENT TYPE=KEYPRESS SELECTOR="input[name=q]" CHARS="hello"
EVENT TYPE=KEYPRESS SELECTOR="input[name=q]" KEY=13
EVENT TYPE=MOUSEOVER XPATH="//nav//a[text()='Products']"
```

#### `CLICK X=n Y=n [CONTENT=...]`
Clicks the element at page coordinates (x, y). The coordinates are adjusted for scrolling.

#### `FRAME F=n` | `FRAME NAME=name`
Selects a frame for the following `TAG` / `EVENT` / `CLICK` commands. `F=0` is the top document, and `F=1..n` counts frames
depth-first. `NAME=` matches the frame's name or id (wildcards allowed). Only same-origin frames can be used.
The selection resets to the top document after `URL GOTO`, `BACK`, `REFRESH` and tab changes.

#### `PROMPT message [var [default]]`
Shows an input dialog and stores the answer in *var*. With only a message, it shows an information box. Cancel stops the macro.
```
PROMPT "Search for?" !VAR1 webview2
```

#### `PAUSE`
Waits until you press **Resume**.

#### `SAVEAS TYPE=EXTRACT|HTM|CPL|TXT|PNG|JPG [FOLDER=path|*] [FILE=name|*|+suffix]`
- `EXTRACT` appends the current extract to a CSV file as one row: each `[EXTRACT]` value becomes a quoted field. The extract is then cleared. `FILE=*` gives `extract.csv`.
- `HTM` / `CPL` save the page HTML (`document.documentElement.outerHTML`). `TXT` saves the visible text.
- `PNG` / `JPG` save a screenshot of the visible area.
- `FOLDER=*`, or no folder, uses `Documents\WebMacros\Downloads`. A relative folder is placed under Downloads.
- `FILE=*` builds a name from the page title. `FILE=+_x` adds `_x` to that name. A missing extension is added automatically.
```
SAVEAS TYPE=EXTRACT FOLDER=* FILE=prices_{{!NOW:yyyymmdd}}.csv
SAVEAS TYPE=HTM FOLDER=C:\temp FILE=*
```

#### `SCREENSHOT TYPE=PAGE|BROWSER [FOLDER=...] [FILE=...]`
Saves a PNG screenshot. `PAGE` captures the whole page through the DevTools protocol. `BROWSER` captures the visible area.

#### `ONDIALOG POS=n BUTTON=OK|CANCEL|YES|NO [CONTENT=text]`
Sets the answer for the *n*-th JavaScript dialog (`alert`, `confirm`, `prompt`, `beforeunload`) that appears from now on.
`CONTENT` is the text typed into a `prompt()`. Each answer is used once, and POS=2 then moves up to POS=1. A dialog
without an answer is shown to you normally.
```
ONDIALOG POS=1 BUTTON=OK
TAG POS=1 TYPE=BUTTON ATTR=TXT:Delete
```

#### `CLEAR [COOKIES|CACHE]`
Clears cookies and cache (all site data by default) for the WebMacros browser profile.

#### `FILTER TYPE=IMAGES STATUS=ON|OFF`
Blocks image downloads, or stops blocking them. `FILTER TYPE=NONE` also stops blocking.

#### `STOPWATCH ID=name` / `STOPWATCH START ID=name` / `STOPWATCH STOP ID=name` / `STOPWATCH LABEL=name`
With only `ID=`, the first call starts the stopwatch and the second stops it. Stopping stores the elapsed seconds in `!STOPWATCHTIME`
and logs them. `LABEL=` logs the time since the macro started.
```
STOPWATCH ID=login
TAG POS=1 TYPE=BUTTON ATTR=TXT:Log<SP>in
STOPWATCH ID=login
ADD !EXTRACT {{!STOPWATCHTIME}}
```

#### `SEARCH SOURCE=TXT:text|REGEXP:pattern [IGNORE_CASE=YES] [EXTRACT=$n]`
Searches the page HTML. It fails if nothing matches. With `EXTRACT=`, `$1`… are replaced by the capture groups and the result
is added to `!EXTRACT`.
```
SEARCH SOURCE=REGEXP:Order<SP>#(\d+) EXTRACT=$1
```

#### `FILEDELETE NAME=path`
Deletes a file. A relative path is resolved against the Downloads folder.

### Loops and datasources

```
' customers.csv: header line + one customer per line. Play Loop with Max = number of lines.
SET !LOOP 2
SET !DATASOURCE customers.csv
SET !DATASOURCE_LINE {{!LOOP}}
URL GOTO=https://httpbin.org/forms/post
TAG POS=1 TYPE=INPUT:TEXT ATTR=NAME:custname CONTENT={{!COL1}}
TAG POS=1 TYPE=INPUT:EMAIL ATTR=NAME:custemail CONTENT={{!COL3}}
TAG POS=1 TYPE=BUTTON ATTR=TXT:Submit<SP>order
```
The CSV reader follows RFC 4180: quoted fields, `""` escapes, and newlines inside quotes are handled.

### Errors

A failing line stops the macro. WebMacros highlights the line and reports `Line n: message`. `SET !ERRORIGNORE YES`
logs the error and continues instead, and `SET !ERRORIGNORE NO` switches this off again. Syntax errors are reported
before anything runs.

## Not implemented / differences from iMacros

These commands are **parsed but not implemented**. They are skipped with a warning in the log:
`SIZE`, `DS`, `WINCLICK`, `IMAGECLICK`, `IMAGESEARCH`, `PROXY`, `ONDOWNLOAD`, `ONLOGIN`, `ONCERTIFICATEDIALOG`,
`ONERRORDIALOG`, `ONSECURITYDIALOG`, `ONWEBPAGEDIALOG`, `ONPRINT`, `PRINT`, `SAVEITEM`, `CMDLINE`, `DISCONNECT`,
`REDIAL`, and the legacy `EXTRACT` command.

Also not supported:

- `SAVEAS TYPE=MHT` and `TYPE=BMP` are accepted but skipped. `TYPE=CPL` saves only the HTML, without images or CSS.
- `FILTER TYPE=FLASH` and `TYPE=POPUPS` are skipped. Popups always open as new tabs.
- Cross-origin iframes can't be used with `FRAME`/`TAG`, because the finder script runs in the top document.
- The recorder records only the top-level document (no iframes). It does not record hover, drag, or file uploads, and it does not record navigations caused by clicks (those clicks are recorded as `TAG`). Password fields are recorded in plain text, with a warning comment.
- File inputs can't be filled (`CONTENT=` on `INPUT:FILE` is an error).
- Macros are not encrypted, and there is no `!ENCRYPTION`, scripting interface, or command-line player.
- Matching of `TXT:` and other attribute values is case-sensitive.
