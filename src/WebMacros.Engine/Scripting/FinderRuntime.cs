namespace WebMacros.Engine.Scripting;

/// <summary>
/// The JavaScript runtime shared by playback (TAG/EVENT/CLICK/FRAME) and the recorder, so element matching
/// semantics are identical in both directions. It defines a single global-free object <c>__wm</c>.
/// Only ES5-level DOM APIs are used so the scripts also run in the test DOM (Jint).
/// </summary>
public static class FinderRuntime
{
    public const string Source = """
var __wm = (function () {
  function WmError(message, retry) { this.message = message; this.retry = !!retry; this.__wm = true; }
  function notFound(msg) { return new WmError(msg, true); }
  function fatal(msg) { return new WmError(msg, false); }

  function norm(s) { return (s === null || s === undefined ? '' : String(s)).replace(/[\s\u00a0]+/g, ' ').replace(/^ | $/g, ''); }
  var reCache = {};
  function wildRe(p) {
    if (reCache[p]) return reCache[p];
    var esc = p.replace(/[.+?^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '[\\s\\S]*');
    return (reCache[p] = new RegExp('^' + esc + '$'));
  }
  function matchStr(pattern, actual) {
    if (pattern === '*') return true;
    if (actual === null || actual === undefined) return false;
    return wildRe(norm(pattern)).test(norm(actual));
  }
  function lower(s) { return (s || '').toLowerCase(); }
  function tagOf(el) { return (el.tagName || el.nodeName || '').toUpperCase(); }
  function attr(el, name) { return el.getAttribute ? el.getAttribute(name) : null; }

  function textOf(el) {
    var t = el.innerText;
    if (t === undefined || t === null) t = el.textContent;
    return norm(t);
  }

  // All candidate values an element exposes for an ATTR key (any one matching is enough).
  function attrValues(el, key) {
    var k = lower(key), tn = tagOf(el), v, out = [];
    if (k === 'txt') {
      if (tn === 'INPUT') { v = el.value; if (v === undefined || v === null) v = attr(el, 'value'); return [v || '']; }
      if (tn === 'SELECT') { return [textOf(el)]; }
      return [textOf(el)];
    }
    if (k === 'href') {
      v = attr(el, 'href'); if (v === null) return [];
      out.push(v); if (el.href && el.href !== v) out.push(el.href); return out;
    }
    if (k === 'src') {
      v = attr(el, 'src'); if (v === null) return [];
      out.push(v); if (el.src && el.src !== v) out.push(el.src); return out;
    }
    if (k === 'class') {
      v = attr(el, 'class'); if (v === null) return [];
      out.push(v); var parts = norm(v).split(' ');
      for (var i = 0; i < parts.length; i++) if (parts[i]) out.push(parts[i]);
      return out;
    }
    if (k === 'value') {
      v = attr(el, 'value'); if (v !== null) out.push(v);
      if (el.value !== undefined && el.value !== null && el.value !== v) out.push(String(el.value));
      return out;
    }
    v = attr(el, k);
    return v === null ? [] : [v];
  }

  function condsMatch(el, conds) {
    if (!conds) return true;
    for (var i = 0; i < conds.length; i++) {
      var c = conds[i];
      if (c.key === '*') continue;
      var vals = attrValues(el, c.key), ok = false;
      for (var j = 0; j < vals.length && !ok; j++) ok = matchStr(c.value, vals[j]);
      if (!ok && c.value === '*' && vals.length > 0) ok = true;
      if (!ok) return false;
    }
    return true;
  }

  function subTypeOf(el) {
    var tn = tagOf(el);
    if (tn === 'INPUT') return (attr(el, 'type') || 'text').toUpperCase();
    if (tn === 'BUTTON') return (attr(el, 'type') || 'submit').toUpperCase();
    return (attr(el, 'type') || '').toUpperCase();
  }

  function typeMatches(el, type) {
    if (!type) return true;
    var tn = tagOf(el);
    if (type.tag !== '*' && tn !== type.tag) return false;
    if (type.sub && type.sub !== '*' && subTypeOf(el) !== type.sub) return false;
    return true;
  }

  // iMacros-style type string for an element, e.g. INPUT:TEXT, A, BUTTON:SUBMIT.
  function typeString(el) {
    var tn = tagOf(el);
    if (tn === 'INPUT' || tn === 'BUTTON') return tn + ':' + subTypeOf(el);
    return tn;
  }

  function formOf(el) {
    var p = el.parentNode;
    while (p) { if (tagOf(p) === 'FORM') return p; p = p.parentNode; }
    return null;
  }

  function frameList(win, out) {
    var fr;
    try { fr = win.frames; } catch (e) { return out; }
    if (!fr) return out;
    for (var i = 0; i < fr.length; i++) {
      var f = fr[i];
      out.push(f);
      try { if (f.document) frameList(f, out); } catch (e) { }
    }
    return out;
  }

  function frameByName(win, name) {
    var docs = [win];
    var list = frameList(win, []);
    for (var i = 0; i < list.length; i++) docs.push(list[i]);
    for (var d = 0; d < docs.length; d++) {
      var doc;
      try { doc = docs[d].document; } catch (e) { continue; }
      if (!doc) continue;
      var all = doc.getElementsByTagName('*');
      for (var k = 0; k < all.length; k++) {
        var t = tagOf(all[k]);
        if ((t === 'IFRAME' || t === 'FRAME') && (matchStr(name, attr(all[k], 'name')) || matchStr(name, attr(all[k], 'id')))) return all[k].contentWindow;
      }
    }
    return null;
  }

  function resolveWin(frame) {
    if (!frame || (frame.index === 0 && !frame.name)) return window;
    var w;
    if (frame.name) {
      w = frameByName(window, frame.name);
      if (!w) throw notFound('Frame NAME=' + frame.name + ' not found');
    } else {
      var list = frameList(window, []);
      w = list[frame.index - 1];
      if (!w) throw notFound('Frame F=' + frame.index + ' not found (page has ' + list.length + ' frame(s))');
    }
    try { if (!w.document) throw 0; } catch (e) { throw fatal('Frame is cross-origin and cannot be accessed'); }
    return w;
  }

  function toArray(list) { var a = []; for (var i = 0; i < list.length; i++) a.push(list[i]); return a; }

  function matches(el, spec) {
    if (!typeMatches(el, spec.type)) return false;
    if (!condsMatch(el, spec.attrs)) return false;
    if (spec.form) { var f = formOf(el); if (!f || !condsMatch(f, spec.form)) return false; }
    return true;
  }

  // All elements matching a spec (TYPE/ATTR/FORM, XPATH or SELECTOR) in document order.
  function findAll(doc, spec) {
    if (spec.xpath) {
      var r;
      try { r = doc.evaluate(spec.xpath, doc, null, 7, null); } catch (e) { throw fatal('Invalid XPath: ' + spec.xpath); }
      var a = [];
      for (var i = 0; i < r.snapshotLength; i++) { var n = r.snapshotItem(i); if (n.nodeType === undefined || n.nodeType === 1) a.push(n); }
      return a;
    }
    if (spec.selector) {
      try { return toArray(doc.querySelectorAll(spec.selector)); } catch (e) { throw fatal('Invalid CSS selector: ' + spec.selector); }
    }
    var all = doc.getElementsByTagName('*'), out = [];
    for (var j = 0; j < all.length; j++) if (matches(all[j], spec)) out.push(all[j]);
    return out;
  }

  function locate(win, spec) {
    var doc = win.document;
    if (spec.relative) {
      var anchor = win.__wmAnchor;
      if (!anchor) throw fatal('POS=R' + spec.pos + ' needs a previous TAG on this page as anchor');
      var all = doc.getElementsByTagName('*'), idx = -1, i;
      for (i = 0; i < all.length; i++) if (all[i] === anchor) { idx = i; break; }
      if (idx < 0) throw fatal('Anchor element of relative POS is no longer in the page');
      var n = 0;
      if (spec.pos > 0) {
        for (i = idx + 1; i < all.length; i++) if (matches(all[i], spec) && ++n === spec.pos) return all[i];
      } else {
        for (i = idx - 1; i >= 0; i--) if (matches(all[i], spec) && ++n === -spec.pos) return all[i];
      }
      throw notFound('Element not found (relative POS=R' + spec.pos + ')');
    }
    var list = findAll(doc, spec);
    if (list.length < spec.pos) throw notFound('Element not found' + (list.length ? ' (only ' + list.length + ' match(es), POS=' + spec.pos + ')' : ''));
    return list[spec.pos - 1];
  }

  function makeEvent(el, type, init) {
    var doc = el.ownerDocument || document, w = doc.defaultView || window, ev;
    init = init || {};
    var opts = { bubbles: true, cancelable: true };
    for (var k in init) if (Object.prototype.hasOwnProperty.call(init, k)) opts[k] = init[k];
    try {
      if (/^(click|dblclick|mouse)/.test(type)) { opts.view = w; ev = new w.MouseEvent(type, opts); }
      else if (/^key/.test(type)) ev = new w.KeyboardEvent(type, opts);
      else ev = new w.Event(type, opts);
    } catch (e) { ev = new Event(type, opts); }
    if (init.keyCode !== undefined) {
      try { Object.defineProperty(ev, 'keyCode', { get: function () { return init.keyCode; } }); } catch (e) { }
      try { Object.defineProperty(ev, 'which', { get: function () { return init.keyCode; } }); } catch (e) { }
    }
    return ev;
  }
  function fire(el, type, init) { return el.dispatchEvent(makeEvent(el, type, init)); }

  function setNativeValue(el, v) {
    var proto = Object.getPrototypeOf(el);
    var desc = proto ? Object.getOwnPropertyDescriptor(proto, 'value') : null;
    if (desc && desc.set) desc.set.call(el, v); else el.value = v;
  }

  function focusEl(el) {
    try { if (el.scrollIntoView) el.scrollIntoView({ block: 'center', inline: 'center' }); } catch (e) { }
    try { if (el.focus) el.focus(); } catch (e) { }
  }

  function clickEl(el) {
    focusEl(el);
    fire(el, 'mouseover'); fire(el, 'mousedown'); fire(el, 'mouseup');
    if (typeof el.click === 'function') el.click(); else fire(el, 'click');
  }

  function selectOptions(el, content) {
    var parts = content.split(':'), i, allPrefixed = true;
    for (i = 0; i < parts.length; i++) if (!/^[%$#]/.test(parts[i])) allPrefixed = false;
    if (!allPrefixed) parts = [content];
    var opts = el.options;
    if (el.multiple) for (i = 0; i < opts.length; i++) opts[i].selected = false;
    for (var p = 0; p < parts.length; p++) {
      var part = parts[p], idx = -1, c = part.charAt(0), want = part.substring(1);
      if (c === '#') idx = parseInt(want, 10) - 1;
      else if (c === '%') { for (i = 0; i < opts.length; i++) if (matchStr(want, opts[i].value)) { idx = i; break; } }
      else if (c === '$') { for (i = 0; i < opts.length; i++) if (matchStr(want, opts[i].text)) { idx = i; break; } }
      else {
        for (i = 0; i < opts.length; i++) if (matchStr(part, opts[i].value)) { idx = i; break; }
        if (idx < 0) for (i = 0; i < opts.length; i++) if (matchStr(part, opts[i].text)) { idx = i; break; }
      }
      if (idx < 0 || idx >= opts.length) throw fatal('Option "' + part + '" not found in SELECT');
      opts[idx].selected = true;
      if (!el.multiple) el.selectedIndex = idx;
    }
    fire(el, 'input'); fire(el, 'change');
  }

  function setContent(el, content) {
    var tn = tagOf(el), type = lower(attr(el, 'type'));
    if (/^EVENT:/i.test(content)) {
      var ev = content.substring(6).toUpperCase();
      if (ev === 'MOUSEOVER') { fire(el, 'mouseover'); fire(el, 'mouseenter'); return; }
      if (ev === 'CLICK') { clickEl(el); return; }
      if (ev === 'FAIL_IF_FOUND') throw fatal('Element was found (CONTENT=EVENT:FAIL_IF_FOUND)');
      throw fatal('Unsupported CONTENT=EVENT:' + ev);
    }
    if (tn === 'SELECT') { focusEl(el); selectOptions(el, content); return; }
    if (tn === 'INPUT' && (type === 'checkbox' || type === 'radio')) {
      var want = /^(yes|true|on|1|checked)$/i.test(content);
      if (!!el.checked !== want) {
        if (type === 'radio' && !want) { el.checked = false; fire(el, 'change'); }
        else { clickEl(el); if (!!el.checked !== want) { el.checked = want; fire(el, 'input'); fire(el, 'change'); } }
      }
      return;
    }
    if (tn === 'INPUT' && type === 'file') throw fatal('File inputs cannot be filled by script');
    if (tn === 'INPUT' || tn === 'TEXTAREA') {
      focusEl(el);
      setNativeValue(el, content);
      fire(el, 'input'); fire(el, 'change');
      return;
    }
    if (el.isContentEditable) { focusEl(el); el.textContent = content; fire(el, 'input'); return; }
    throw fatal('CONTENT= is not supported for element ' + typeString(el));
  }

  function submitFormOf(el) {
    var f = el.form || formOf(el);
    if (!f) return false;
    if (typeof f.requestSubmit === 'function') f.requestSubmit(); else f.submit();
    return true;
  }

  function pressKey(el, keyCode, ch) {
    var init = { keyCode: keyCode };
    if (ch) { init.key = ch; init.charCode = ch.charCodeAt(0); }
    else if (keyCode === 13) init.key = 'Enter';
    var ok = fire(el, 'keydown', init);
    if (ok) ok = fire(el, 'keypress', init);
    var tn = tagOf(el);
    if (ok && (tn === 'INPUT' || tn === 'TEXTAREA')) {
      if (ch) { setNativeValue(el, (el.value || '') + ch); fire(el, 'input'); }
      else if (keyCode === 8) { setNativeValue(el, (el.value || '').slice(0, -1)); fire(el, 'input'); }
    }
    fire(el, 'keyup', init);
    if (ok && keyCode === 13 && tn === 'INPUT') { fire(el, 'change'); submitFormOf(el); }
  }

  function extract(el, what) {
    var tn = tagOf(el), v;
    switch (what) {
      case 'TXT':
        if (tn === 'INPUT' || tn === 'TEXTAREA') return el.value === undefined || el.value === null ? '' : String(el.value);
        if (tn === 'SELECT') { var o = el.options[el.selectedIndex]; return o ? norm(o.text) : ''; }
        return textOf(el);
      case 'TXTALL':
        if (tn === 'SELECT') { var t = []; for (var i = 0; i < el.options.length; i++) t.push(norm(el.options[i].text)); return t.join('[OPTION]'); }
        return textOf(el);
      case 'HTM': return norm(el.outerHTML);
      case 'HREF': v = attr(el, 'href'); if (v === null) return '#EANF#'; return el.href || v;
      case 'CHECKED': return el.checked ? 'YES' : 'NO';
      default: v = attr(el, lower(what)); return v === null ? '#EANF#' : v;
    }
  }

  function describeResult(el) { return { tag: typeString(el) }; }

  function runTag(spec) {
    var win = resolveWin(spec.frame);
    var el = locate(win, spec);
    win.__wmAnchor = el;
    var res = { ok: true, found: true, tag: typeString(el) };
    if (spec.extract) res.value = extract(el, spec.extract);
    else if (spec.content !== undefined && spec.content !== null) {
      setContent(el, spec.content);
      if (spec.pressEnter) { focusEl(el); pressKey(el, 13); }
    }
    else clickEl(el);
    return res;
  }

  function runEvent(spec) {
    var win = resolveWin(spec.frame), doc = win.document, el;
    if (spec.xpath || spec.selector) {
      var list = findAll(doc, spec);
      if (!list.length) throw notFound('Element not found for EVENT');
      el = list[0];
    } else {
      el = doc.activeElement || doc.body;
      if (!el) throw notFound('No focused element for EVENT');
    }
    var t = spec.eventType;
    if (t === 'CLICK') clickEl(el);
    else if (t === 'DBLCLICK') { clickEl(el); fire(el, 'dblclick'); }
    else if (t === 'MOUSEOVER') { fire(el, 'mouseover'); fire(el, 'mouseenter'); }
    else if (t === 'FOCUS') focusEl(el);
    else if (t === 'BLUR') { try { el.blur(); } catch (e) { fire(el, 'blur'); } }
    else if (t === 'SUBMIT') { if (!submitFormOf(el)) throw fatal('Element is not inside a form'); }
    else if (t === 'KEYPRESS' || t === 'KEYDOWN' || t === 'KEYUP') {
      focusEl(el);
      if (spec.chars) { for (var i = 0; i < spec.chars.length; i++) pressKey(el, spec.chars.charCodeAt(i), spec.chars.charAt(i)); }
      else if (spec.key !== undefined && spec.key !== null) pressKey(el, spec.key, null);
      else throw fatal('EVENT TYPE=' + t + ' requires KEY= or CHARS=');
    }
    else fire(el, t.toLowerCase());
    return { ok: true, found: true, tag: typeString(el) };
  }

  function runPoint(spec) {
    var win = resolveWin(spec.frame), doc = win.document;
    var el = doc.elementFromPoint(spec.x - (win.scrollX || 0), spec.y - (win.scrollY || 0));
    if (!el) throw notFound('No element at X=' + spec.x + ' Y=' + spec.y);
    if (spec.content !== undefined && spec.content !== null) setContent(el, spec.content); else clickEl(el);
    return { ok: true, found: true, tag: typeString(el) };
  }

  function run(spec) {
    try {
      switch (spec.kind) {
        case 'tag': return runTag(spec);
        case 'event': return runEvent(spec);
        case 'point': return runPoint(spec);
        case 'frame': resolveWin(spec.frame); return { ok: true, found: true };
        default: return { ok: false, found: false, retry: false, error: 'Unknown spec kind ' + spec.kind };
      }
    } catch (e) {
      if (e && e.__wm) return { ok: false, found: false, retry: e.retry, error: e.message };
      return { ok: false, found: false, retry: false, error: 'Script error: ' + (e && e.message ? e.message : String(e)) };
    }
  }

  return { run: run, findAll: findAll, typeString: typeString, attrValues: attrValues, textOf: textOf, norm: norm, matchStr: matchStr, formOf: formOf };
})();
""";
}
