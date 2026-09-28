// A tiny DOM good enough to run WebMacros' finder runtime inside Jint.
// Pages are built with h(tag, attrs, children...) and mounted with setPage(root).
var __log = [];
function Event(type, init) { init = init || {}; this.type = type; this.bubbles = !!init.bubbles; this.key = init.key; this.charCode = init.charCode; this.defaultPrevented = false; }
Event.prototype.preventDefault = function () { this.defaultPrevented = true; };
function MouseEvent(type, init) { Event.call(this, type, init); }
MouseEvent.prototype = Object.create(Event.prototype);
function KeyboardEvent(type, init) { Event.call(this, type, init); }
KeyboardEvent.prototype = Object.create(Event.prototype);

function TextNode(text) { this.nodeType = 3; this.text = text; this.parentNode = null; }

function El(tag, attrs) {
  this.nodeType = 1;
  this.tagName = tag.toUpperCase();
  this.nodeName = this.tagName;
  this._attrs = {};
  for (var k in (attrs || {})) this._attrs[k.toLowerCase()] = String(attrs[k]);
  this.childNodes = [];
  this.parentNode = null;
  this.events = [];
  this.listeners = {};
  this._value = this._attrs.value !== undefined ? this._attrs.value : '';
  this.checked = this._attrs.checked !== undefined;
  this.selected = this._attrs.selected !== undefined;
  this.multiple = this._attrs.multiple !== undefined;
  this._selectedIndex = -1;
  this.isContentEditable = this._attrs.contenteditable === 'true';
}
El.prototype.getAttribute = function (n) { n = n.toLowerCase(); return Object.prototype.hasOwnProperty.call(this._attrs, n) ? this._attrs[n] : null; };
El.prototype.setAttribute = function (n, v) { this._attrs[n.toLowerCase()] = String(v); };
El.prototype.appendChild = function (c) { c.parentNode = this; this.childNodes.push(c); return c; };
El.prototype.addEventListener = function (t, f) { (this.listeners[t] = this.listeners[t] || []).push(f); };
El.prototype.removeEventListener = function (t, f) { var l = this.listeners[t] || []; var i = l.indexOf(f); if (i >= 0) l.splice(i, 1); };
El.prototype.dispatchEvent = function (ev) {
  this.events.push(ev.type);
  __log.push(ev.type + ':' + (this.getAttribute('id') || this.tagName));
  ev.target = this;
  var node = this;
  while (node) {
    var l = (node.listeners && node.listeners[ev.type]) || [];
    for (var i = 0; i < l.length; i++) l[i].call(node, ev);
    if (!ev.bubbles) break;
    node = node.parentNode;
  }
  return !ev.defaultPrevented;
};
El.prototype.click = function () {
  var t = (this.getAttribute('type') || '').toLowerCase();
  if (this.tagName === 'INPUT' && t === 'checkbox') this.checked = !this.checked;
  if (this.tagName === 'INPUT' && t === 'radio') this.checked = true;
  this.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  if ((this.tagName === 'BUTTON' && t !== 'button') || (this.tagName === 'INPUT' && t === 'submit')) { var f = this.form; if (f) f.submitted = true; }
  if (this.tagName === 'A' && this.getAttribute('href')) window.__clickedHref = this.href;
};
El.prototype.focus = function () { document.activeElement = this; this.events.push('focus'); };
El.prototype.blur = function () { this.events.push('blur'); };
El.prototype.scrollIntoView = function () { };
El.prototype.requestSubmit = function () { this.submitted = true; };
El.prototype.submit = function () { this.submitted = true; };
Object.defineProperty(El.prototype, 'textContent', {
  get: function () { var s = ''; for (var i = 0; i < this.childNodes.length; i++) { var c = this.childNodes[i]; s += c.nodeType === 3 ? c.text : c.textContent; } return s; },
  set: function (v) { this.childNodes = [new TextNode(v)]; }
});
Object.defineProperty(El.prototype, 'innerText', { get: function () { return this.textContent; } });
Object.defineProperty(El.prototype, 'value', {
  get: function () {
    if (this.tagName === 'SELECT') { var o = this.options[this.selectedIndex]; return o ? o.value : ''; }
    if (this.tagName === 'OPTION') { var v = this.getAttribute('value'); return v === null ? this.textContent : v; }
    if (this.tagName === 'TEXTAREA' && this._value === '') return this.textContent;
    return this._value;
  },
  set: function (v) { this._value = String(v); this.events.push('value=' + v); }
});
Object.defineProperty(El.prototype, 'text', { get: function () { return this.textContent; } });
Object.defineProperty(El.prototype, 'id', { get: function () { return this.getAttribute('id') || ''; } });
Object.defineProperty(El.prototype, 'href', { get: function () { var h = this.getAttribute('href'); if (h === null) return undefined; return /^[a-z]+:/i.test(h) ? h : 'https://example.test' + (h.charAt(0) === '/' ? '' : '/') + h; } });
Object.defineProperty(El.prototype, 'options', { get: function () { var a = []; walk(this, function (e) { if (e.tagName === 'OPTION') a.push(e); }); return a; } });
Object.defineProperty(El.prototype, 'selectedIndex', {
  get: function () { var o = this.options; for (var i = 0; i < o.length; i++) if (o[i].selected) return i; return o.length ? 0 : -1; },
  set: function (i) { var o = this.options; for (var j = 0; j < o.length; j++) o[j].selected = (j === i); }
});
Object.defineProperty(El.prototype, 'form', { get: function () { var p = this.parentNode; while (p) { if (p.tagName === 'FORM') return p; p = p.parentNode; } return null; } });
Object.defineProperty(El.prototype, 'outerHTML', {
  get: function () {
    var a = ''; for (var k in this._attrs) a += ' ' + k + '="' + this._attrs[k] + '"';
    var inner = ''; for (var i = 0; i < this.childNodes.length; i++) { var c = this.childNodes[i]; inner += c.nodeType === 3 ? c.text : c.outerHTML; }
    var t = this.tagName.toLowerCase(); return '<' + t + a + '>' + inner + '</' + t + '>';
  }
});
Object.defineProperty(El.prototype, 'ownerDocument', { get: function () { var n = this; while (n.parentNode) n = n.parentNode; return n.__doc || document; } });

function walk(el, f) { for (var i = 0; i < el.childNodes.length; i++) { var c = el.childNodes[i]; if (c.nodeType === 1) { f(c); walk(c, f); } } }

function h(tag, attrs) {
  var el = new El(tag, attrs);
  for (var i = 2; i < arguments.length; i++) {
    var c = arguments[i];
    if (c === null || c === undefined) continue;
    el.appendChild(typeof c === 'string' ? new TextNode(c) : c);
  }
  return el;
}

function matchesSimpleSelector(el, sel) {
  var m = /^([a-z0-9]*)(#[\w-]+)?(\.[\w-]+)?(\[([\w-]+)="?([^"\]]*)"?\])?$/i.exec(sel.trim());
  if (!m) throw new Error('unsupported selector ' + sel);
  if (m[1] && el.tagName !== m[1].toUpperCase()) return false;
  if (m[2] && el.getAttribute('id') !== m[2].substring(1)) return false;
  if (m[3] && (' ' + (el.getAttribute('class') || '') + ' ').indexOf(' ' + m[3].substring(1) + ' ') < 0) return false;
  if (m[4] && el.getAttribute(m[5]) !== m[6]) return false;
  return true;
}

function makeDocument(root, win) {
  var doc = {
    documentElement: root,
    body: null,
    activeElement: null,
    title: '',
    defaultView: win,
    getElementsByTagName: function (t) { var a = []; if (t === '*' || root.tagName === t.toUpperCase()) a.push(root); walk(root, function (e) { if (t === '*' || e.tagName === t.toUpperCase()) a.push(e); }); return a; },
    querySelectorAll: function (sel) { if (sel.indexOf('!!') >= 0) throw new Error('bad selector'); return this.getElementsByTagName('*').filter(function (e) { return matchesSimpleSelector(e, sel); }); },
    elementFromPoint: function (x, y) { var all = this.getElementsByTagName('*'); for (var i = 0; i < all.length; i++) if (all[i].getAttribute('data-pt') === x + ',' + y) return all[i]; return null; },
    // Supports //tag[@attr='value'] and //tag only.
    evaluate: function (xp) {
      var m = /^\/\/(\w+|\*)(\[@([\w-]+)=['"]([^'"]*)['"]\])?$/.exec(xp);
      if (!m) throw new Error('unsupported xpath');
      var res = this.getElementsByTagName(m[1]).filter(function (e) { return !m[2] || e.getAttribute(m[3]) === m[4]; });
      return { snapshotLength: res.length, snapshotItem: function (i) { return res[i]; } };
    }
  };
  root.__doc = doc;
  walk(root, function (e) { if (e.tagName === 'BODY') doc.body = e; if (e.tagName === 'TITLE') doc.title = e.textContent; });
  return doc;
}

var window = { frames: [], scrollX: 0, scrollY: 0, Event: Event, MouseEvent: MouseEvent, KeyboardEvent: KeyboardEvent };
window.window = window;
window.top = window;
var document = makeDocument(h('html', null, h('body')), window);
window.document = document;

function setPage(root) {
  document = makeDocument(root, window);
  window.document = document;
  window.frames = [];
  window.__wmAnchor = undefined;
  return document;
}

// Adds a same-origin frame whose content is `root`; the frame's <iframe> element gets name/id.
function addFrame(name, root) {
  var fw = { frames: [], scrollX: 0, scrollY: 0, Event: Event, MouseEvent: MouseEvent, KeyboardEvent: KeyboardEvent };
  fw.document = makeDocument(root, fw);
  var iframe = h('iframe', { name: name, id: name });
  iframe.contentWindow = fw;
  document.body.appendChild(iframe);
  window.frames.push(fw);
  return fw;
}

function byId(id, doc) { var all = (doc || document).getElementsByTagName('*'); for (var i = 0; i < all.length; i++) if (all[i].getAttribute('id') === id) return all[i]; return null; }

// Dialog hooks (the C# fake driver provides __dialog(kind, message, default) -> string|null).
function alert(m) { if (typeof __dialog === 'function') __dialog('alert', String(m), ''); }
function confirm(m) { return typeof __dialog === 'function' ? __dialog('confirm', String(m), '') !== null : false; }
function prompt(m, d) { return typeof __dialog === 'function' ? __dialog('prompt', String(m), d === undefined ? '' : String(d)) : null; }
window.alert = alert; window.confirm = confirm; window.prompt = prompt;
