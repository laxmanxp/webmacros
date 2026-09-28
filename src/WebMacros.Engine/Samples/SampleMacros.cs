namespace WebMacros.Engine.Samples;

public sealed record SampleFile(string FileName, string Content);

/// <summary>Sample macros and datasources seeded into Documents\WebMacros on first run.</summary>
public static class SampleMacros
{
    public static readonly SampleFile DuckDuckGo = new("01-DuckDuckGo-Search-Extract.iim", """
VERSION BUILD=1000
' Searches DuckDuckGo (HTML version) and extracts the titles of the first 5 results.
' Results are shown in the log and appended to Documents\WebMacros\Downloads\ddg-results.csv
' Note: DuckDuckGo may occasionally show a bot check instead of results.
SET !EXTRACT_TEST_POPUP NO
SET !TIMEOUT_STEP 10
SET query "webview2 browser automation"
TAB T=1
URL GOTO=https://html.duckduckgo.com/html/
' <ENTER> at the end of CONTENT submits the form
TAG POS=1 TYPE=INPUT:TEXT ATTR=NAME:q CONTENT={{query}}<ENTER>
TAG POS=1 TYPE=A ATTR=CLASS:result__a EXTRACT=TXT
TAG POS=2 TYPE=A ATTR=CLASS:result__a EXTRACT=TXT
TAG POS=3 TYPE=A ATTR=CLASS:result__a EXTRACT=TXT
' Errors on the last two are ignored (fewer results => #EANF#)
SET !ERRORIGNORE YES
SET !TIMEOUT_STEP 2
TAG POS=4 TYPE=A ATTR=CLASS:result__a EXTRACT=TXT
TAG POS=5 TYPE=A ATTR=CLASS:result__a EXTRACT=TXT
SET !ERRORIGNORE NO
SAVEAS TYPE=EXTRACT FOLDER=* FILE=ddg-results.csv
""");

    public static readonly SampleFile FormFromCsv = new("02-Fill-Form-From-CSV.iim", """
VERSION BUILD=1000
' Fills the httpbin.org pizza order form once per row of customers.csv
' (Documents\WebMacros\Datasources\customers.csv) and extracts the server's echo.
' Play with "Play Loop" and Max = 4: line 1 is the CSV header, so we start at !LOOP 2.
SET !EXTRACT_TEST_POPUP NO
SET !LOOP 2
SET !DATASOURCE customers.csv
SET !DATASOURCE_LINE {{!LOOP}}
URL GOTO=https://httpbin.org/forms/post
TAG POS=1 TYPE=INPUT:TEXT ATTR=NAME:custname CONTENT={{!COL1}}
TAG POS=1 TYPE=INPUT:TEL ATTR=NAME:custtel CONTENT={{!COL2}}
TAG POS=1 TYPE=INPUT:EMAIL ATTR=NAME:custemail CONTENT={{!COL3}}
TAG POS=1 TYPE=INPUT:RADIO ATTR=NAME:size&&VALUE:{{!COL4}} CONTENT=YES
TAG POS=1 TYPE=INPUT:CHECKBOX ATTR=NAME:topping&&VALUE:{{!COL5}} CONTENT=YES
TAG POS=1 TYPE=INPUT:TIME ATTR=NAME:delivery CONTENT={{!COL6}}
TAG POS=1 TYPE=TEXTAREA ATTR=NAME:comments CONTENT={{!COL7}}
TAG POS=1 TYPE=BUTTON ATTR=TXT:Submit<SP>order
' httpbin answers with JSON, which the browser shows inside a <pre> element
ADD !EXTRACT {{!COL1}}
TAG POS=1 TYPE=PRE ATTR=* EXTRACT=TXT
SAVEAS TYPE=EXTRACT FOLDER=* FILE=httpbin-orders.csv
""");

    public static readonly SampleFile LoopPages = new("03-Loop-Visit-Pages.iim", """
VERSION BUILD=1000
' Visits a list of pages (play with "Play Loop", Max = 3), measures load time with STOPWATCH
' and saves url, title, seconds and a timestamp to Downloads\visited-pages.csv
SET !EXTRACT_TEST_POPUP NO
SET !ERRORIGNORE YES
SET page EVAL("var urls = ['https://example.com/', 'https://httpbin.org/html', 'https://www.wikipedia.org/']; urls[({{!LOOP}} - 1) % urls.length];")
STOPWATCH ID=load
URL GOTO={{page}}
STOPWATCH ID=load
ADD !EXTRACT {{page}}
TAG POS=1 TYPE=TITLE ATTR=* EXTRACT=TXT
ADD !EXTRACT {{!STOPWATCHTIME}}
ADD !EXTRACT {{!NOW:yyyy-mm-dd<SP>hh:nn:ss}}
SAVEAS TYPE=EXTRACT FOLDER=* FILE=visited-pages.csv
WAIT SECONDS=1
""");

    public static readonly SampleFile DialogsDemo = new("04-Dialogs-Eval-Prompt-Demo.iim", """
VERSION BUILD=1000
' Demonstrates PROMPT, EVAL and ONDIALOG (automatic answers to alert/confirm/prompt).
SET !EXTRACT_TEST_POPUP YES
PROMPT "What is your name?" !VAR1 World
SET !VAR2 EVAL("var n = '{{!VAR1}}'; n.toUpperCase() + ' has ' + n.length + ' letters';")
URL GOTO=https://example.com/
' The next dialog (an alert) is closed with OK automatically
ONDIALOG POS=1 BUTTON=OK
SET shown EVAL("alert('Hello {{!VAR1}}! ONDIALOG closes this alert.'); 'alert handled';")
' Answer a confirm() with CANCEL
ONDIALOG POS=1 BUTTON=CANCEL
SET answer EVAL("confirm('Continue?') ? 'confirm: OK' : 'confirm: CANCEL';")
' Answer a prompt() with OK and a text
ONDIALOG POS=1 BUTTON=OK CONTENT=WebMacros
SET typed EVAL("'prompt: ' + prompt('Which tool is this?', 'unknown');")
' EVAL can also compute numbers and dates
SET sum EVAL("[1, 2, 3, 4].reduce(function (a, b) { return a + b; }, 0);")
ADD sum 10
ADD !EXTRACT {{!VAR2}}
ADD !EXTRACT {{shown}}
ADD !EXTRACT {{answer}}
ADD !EXTRACT {{typed}}
ADD !EXTRACT sum={{sum}}
""");

    public static readonly SampleFile AddRowsScript = new("05-Script-Add-Rows-From-CSV.js", """
// WebMacros JavaScript macro (iMacros-style scripting).
// Opens a small demo page, then uses a while loop that clicks "Add Row" and fills each new row from
// customers.csv (Documents\WebMacros\Datasources). An if-condition checks the extracted status text.
// Press Stop at any time to abort the script.

var MAX_ROWS = 5;
var page =
  "<html><head><title>Rows demo</title></head><body style='font-family:sans-serif'>" +
  "<h2>Order rows</h2><table border='1' cellpadding='4'><thead><tr><th>#</th><th>Name</th><th>Email</th><th>Priority</th></tr></thead>" +
  "<tbody id='rows'></tbody></table><p><button type='button' id='add' onclick='addRow()'>Add Row</button></p>" +
  "<p id='status'>0 rows</p><script>function addRow(){var tb=document.getElementById('rows');var n=tb.rows.length;" +
  "if(n>=" + MAX_ROWS + "){document.getElementById('status').textContent='Maximum rows reached';return;}" +
  "var tr=tb.insertRow();tr.innerHTML='<td>'+(n+1)+'</td><td><input name=name></td><td><input name=email></td>" +
  "<td><input type=checkbox name=priority></td>';document.getElementById('status').textContent=(n+1)+' rows';}</script></body></html>";

if (iimPlay("CODE:URL GOTO=data:text/html;charset=utf-8," + encodeURIComponent(page)) < 0) {
  alert("Could not open the demo page: " + iimGetLastError());
  iimExit();
}

var rows = readCsv("customers.csv");   // [[header...], [row 1...], ...]
console.log("Read " + (rows.length - 1) + " customers from customers.csv");

var i = 1;                               // skip the header line
while (i < rows.length) {
  var customer = rows[i];
  iimDisplay("Adding row " + i + ": " + customer[0]);

  // Click "Add Row" and read the status text the page shows.
  var ret = iimPlay("CODE:TAG POS=1 TYPE=BUTTON ATTR=TXT:Add<SP>Row\n" +
                    "TAG POS=1 TYPE=P ATTR=ID:status EXTRACT=TXT");
  if (ret < 0) {
    console.error("Add Row failed: " + iimGetLastError());
    break;
  }
  var status = iimGetLastExtract(1);
  if (status == "Maximum rows reached") {
    iimDisplay("The page does not accept more rows; stopping after " + (i - 1) + " row(s)");
    break;
  }

  // Fill the new row. iimSet values are available as {{name}} in the next iimPlay only.
  iimSet("row", i);
  iimSet("name", customer[0]);
  iimSet("email", customer[2]);
  ret = iimPlay("CODE:TAG POS={{row}} TYPE=INPUT:TEXT ATTR=NAME:name CONTENT={{name}}\n" +
                "TAG POS={{row}} TYPE=INPUT:TEXT ATTR=NAME:email CONTENT={{email}}");
  if (ret < 0) {
    console.error("Filling row " + i + " failed: " + iimGetLastError());
    break;
  }

  // Condition on the data: large orders get the priority checkbox.
  if (customer[3] == "large") {
    iimSet("row", i);
    iimPlay("CODE:TAG POS={{row}} TYPE=INPUT:CHECKBOX ATTR=NAME:priority CONTENT=YES");
    console.log(customer[0] + " marked as priority");
  }
  i++;
}

// Run JavaScript in the page and use its result in the script.
var count = iimEval("document.getElementsByTagName('tr').length - 1");
iimDisplay("Done: the table has " + count + " row(s)");
""");

    public static readonly SampleFile CustomersCsv = new("customers.csv", """
name,phone,email,size,topping,delivery,comments
Alice Smith,555-0101,alice@example.com,small,bacon,12:00,Ring the bell
Bob Jones,555-0102,bob@example.com,medium,cheese,18:30,"Leave at the door, please"
Carol White,555-0103,carol@example.com,large,mushroom,20:15,No onions
""");

    public static IReadOnlyList<SampleFile> Macros { get; } = new[] { DuckDuckGo, FormFromCsv, LoopPages, DialogsDemo };
    /// <summary>JavaScript (.js) macros.</summary>
    public static IReadOnlyList<SampleFile> Scripts { get; } = new[] { AddRowsScript };
    public static IReadOnlyList<SampleFile> DataSources { get; } = new[] { CustomersCsv };
}
