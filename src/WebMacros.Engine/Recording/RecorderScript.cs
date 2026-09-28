namespace WebMacros.Engine.Recording;

/// <summary>
/// JavaScript injected while recording. It reuses <see cref="Scripting.FinderRuntime"/> to compute, for each
/// candidate ATTR, the POS the element has under exactly the matching rules used at playback, then posts a
/// JSON message (window.chrome.webview.postMessage) that <see cref="RecordedAction.Parse"/> understands.
/// Only the top-level document is recorded.
/// </summary>
public static class RecorderScript
{
    public static string Build() => "(function(){\n" +
        "if (window.top !== window || window.__wmRecorderInstalled) return;\n" +
        "window.__wmRecorderInstalled = true;\n" +
        Scripting.FinderRuntime.Source + "\n" + Listener + "\n})();";

    /// <summary>Script that detaches the recorder from the current document.</summary>
    public const string StopScript = "(function(){ if (window.__wmRecorderStop) window.__wmRecorderStop(); })();";

    private const string Listener = """
var lastClick = 0;
function post(o) { try { window.chrome.webview.postMessage(JSON.stringify(o)); } catch (e) { } }
function candidates(el) {
  var doc = el.ownerDocument, type = __wm.typeString(el), tn = type.split(':')[0], out = [];
  function add(key, value) {
    if (value === null || value === undefined) return;
    value = __wm.norm(value);
    if (!value || value.indexOf('*') >= 0 || value.indexOf('&&') >= 0 || value.length > 80) return;
    var tp = type.indexOf(':') > 0 ? { tag: tn, sub: type.split(':')[1] } : { tag: tn };
    var list = __wm.findAll(doc, { type: tp, attrs: [{ key: key, value: value }] });
    var pos = list.indexOf(el) + 1;
    if (pos > 0) out.push({ key: key, value: value, pos: pos });
  }
  add('ID', el.getAttribute('id'));
  add('NAME', el.getAttribute('name'));
  if (tn === 'A' || tn === 'BUTTON' || tn === 'LABEL' || tn === 'SPAN' || tn === 'LI' || tn === 'TD' || tn === 'H1' || tn === 'H2' || tn === 'H3' || tn === 'DIV' || tn === 'P' ||
      type === 'INPUT:SUBMIT' || type === 'INPUT:BUTTON' || type === 'INPUT:RESET') add('TXT', __wm.attrValues(el, 'TXT')[0]);
  if (tn === 'A') add('HREF', el.getAttribute('href'));
  if (tn === 'IMG') { add('ALT', el.getAttribute('alt')); add('SRC', el.getAttribute('src')); }
  if (tn === 'INPUT' || tn === 'TEXTAREA') add('PLACEHOLDER', el.getAttribute('placeholder'));
  add('CLASS', el.getAttribute('class'));
  var all = __wm.findAll(doc, { type: type.indexOf(':') > 0 ? { tag: tn, sub: type.split(':')[1] } : { tag: tn }, attrs: [] });
  out.push({ key: '*', value: '*', pos: all.indexOf(el) + 1 });
  return { type: type, candidates: out };
}
function interesting(el) {
  // Walk up from the click target to the nearest actionable element.
  var e = el;
  while (e && e.nodeType === 1) {
    var tn = (e.tagName || '').toUpperCase();
    if (tn === 'A' || tn === 'BUTTON' || tn === 'INPUT' || tn === 'SELECT' || tn === 'TEXTAREA' || tn === 'LABEL' || tn === 'IMG' || e.getAttribute('onclick') || e.getAttribute('role') === 'button') return e;
    e = e.parentNode;
  }
  return el && el.nodeType === 1 ? el : null;
}
function onClick(ev) {
  var el = interesting(ev.target); if (!el) return;
  var tn = (el.tagName || '').toUpperCase(), t = (el.getAttribute('type') || 'text').toLowerCase();
  if (tn === 'SELECT' || tn === 'TEXTAREA' || tn === 'OPTION') return;
  if (tn === 'INPUT' && ['checkbox', 'radio', 'submit', 'button', 'reset', 'image'].indexOf(t) < 0) return;
  if (tn === 'INPUT' && (t === 'checkbox' || t === 'radio')) return; // recorded by the change event
  lastClick = Date.now();
  var d = candidates(el); d.action = 'click'; post(d);
}
function onChange(ev) {
  var el = ev.target; if (!el || !el.tagName) return;
  var tn = el.tagName.toUpperCase(), t = (el.getAttribute('type') || 'text').toLowerCase();
  var d = candidates(el);
  if (tn === 'SELECT') {
    var vals = []; for (var i = 0; i < el.options.length; i++) if (el.options[i].selected) vals.push(el.options[i].value);
    d.action = 'select'; d.values = vals;
  } else if (tn === 'INPUT' && (t === 'checkbox' || t === 'radio')) {
    d.action = 'check'; d.checked = !!el.checked;
  } else if (tn === 'INPUT' || tn === 'TEXTAREA') {
    if (t === 'file' || t === 'submit' || t === 'button') return;
    d.action = 'type'; d.value = el.value;
    d.password = t === 'password';
  } else return;
  post(d);
}
function onSubmit(ev) {
  if (Date.now() - lastClick < 1500) return;
  var el = document.activeElement;
  if (!el || !el.tagName || el.tagName.toUpperCase() !== 'INPUT') return;
  var d = candidates(el); d.action = 'submit'; d.value = el.value; post(d);
}
document.addEventListener('click', onClick, true);
document.addEventListener('change', onChange, true);
document.addEventListener('submit', onSubmit, true);
window.__wmRecorderStop = function () {
  document.removeEventListener('click', onClick, true);
  document.removeEventListener('change', onChange, true);
  document.removeEventListener('submit', onSubmit, true);
  window.__wmRecorderInstalled = false;
};
""";
}
