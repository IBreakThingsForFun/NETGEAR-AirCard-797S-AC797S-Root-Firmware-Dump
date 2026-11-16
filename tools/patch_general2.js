#!/usr/bin/env node
// Patches a stock /mnt/hdata/index.html.tmpl to add a "General2" settings
// page at #settings/general2, which edits router.hostName (the real,
// writable key behind the portal's read-only "AC797S Manager" field).
//
// index.html.tmpl is NOT plain HTML. It's a sequence of length-prefixed
// records: 1 opcode byte + a 2-byte little-endian length + that many content
// bytes. Opcode 1 is a plain literal run (raw bytes, pass straight through);
// other opcodes (seen: 2 = symbol/text reference, 4/5 = available/writable
// conditionals, and others) encode dynamic directives whose internal grammar
// is not fully reverse-engineered here. This script only ever touches
// opcode-1 (literal) records and never needs to understand the others, which
// is what keeps the edit safe: splice new literal bytes into an *existing*
// literal record's content, then rewrite that one record's 2-byte length
// field to match. No other record's position or length changes.
//
// Usage: node patch_general2.js <path to pristine index.html.tmpl> <output path>
//
// Deploy the result to the device with `AC797.exe pushbin <output> /tmp/x.bin`
// (base64/heredoc, binary-safe), then as root: `mount -o remount,rw /mnt/hdata`,
// `cp /tmp/x.bin /mnt/hdata/index.html.tmpl`, and restart NetgearWebApp (it
// caches the compiled template in memory, so edits on disk alone don't show
// up until it's restarted) — kill the PID from `ps | grep NetgearWebApp`; it's
// supervised by /usr/sbin/restartNetgearWebApp and comes back on its own.
// Remount `/mnt/hdata` back to `ro` once you're done.

const fs = require('fs');

const [, , srcPath, outPath] = process.argv;
if (!srcPath || !outPath) {
  console.error('Usage: node patch_general2.js <src index.html.tmpl> <out path>');
  process.exit(1);
}
const buf = fs.readFileSync(srcPath);

function record(headerStart) {
  const opcode = buf[headerStart];
  const len = buf.readUInt16LE(headerStart + 1);
  return { headerStart, opcode, len, contentStart: headerStart + 3, contentEnd: headerStart + 3 + len };
}

// --- Record A: the Settings nav <ul> — insert a new "General2" <li> right
// after the existing "About" tab's closing </a></li> ---
const navNeedle = '</a></li>\n\t\t\n\t</ul>\n\t<div class="block">\n\t<div class="section" id="settings_general"';
const navIdx = buf.indexOf(Buffer.from(navNeedle, 'latin1'));
if (navIdx < 0) throw new Error('nav anchor text not found — template layout may have changed');
const recA = record(navIdx - 3);
if (recA.opcode !== 1) throw new Error('recA opcode mismatch: ' + recA.opcode);
const contentA = buf.slice(recA.contentStart, recA.contentEnd).toString('latin1');
const splitA = contentA.indexOf('</a></li>') + '</a></li>'.length;
const navLi = '\n\t\t<li><a id="nav_settings_general2" href="#settings/general2" role="tab" aria-selected="false" aria-controls="settings_general2">General2</a></li>';
const newContentA = contentA.slice(0, splitA) + navLi + contentA.slice(splitA);
console.log('Record A (nav list): old len', recA.len, '-> new len', newContentA.length);

// --- Record B: right before the settings_network panel begins — insert a
// new sibling <div id="settings_general2"> panel with the editable field ---
const netNeedle = '<div class="section" id="settings_network">';
const netIdx = buf.indexOf(Buffer.from(netNeedle, 'latin1'));
if (netIdx < 0) throw new Error('settings_network anchor not found');
let recBHeaderStart = -1;
for (let back = 1; back <= 400; back++) {
  const hs = netIdx - back;
  if (buf[hs] === 1) {
    const r = record(hs);
    if (r.contentEnd >= netIdx && r.contentEnd <= netIdx + 2000 && r.contentStart <= netIdx) { recBHeaderStart = hs; break; }
  }
}
if (recBHeaderStart < 0) throw new Error('enclosing literal record for settings_network not found');
const recB = record(recBHeaderStart);
if (recB.opcode !== 1) throw new Error('recB opcode mismatch: ' + recB.opcode);
const contentB = buf.slice(recB.contentStart, recB.contentEnd).toString('latin1');
const splitB = contentB.indexOf('<div class="section" id="settings_network">');
if (splitB < 0) throw new Error('splitB not found inside recB content');

const panelHtml = [
  '<div class="section" id="settings_general2">',
  '<ul class="nav_section"><li>AC797S Manager (General2)</li></ul>',
  '<div class="block"><div class="subsection">',
  '<fieldset><h4>AC797S Manager (router.hostName)</h4>',
  '<dl><dt>Manager name:</dt>',
  '<dd><input type="text" id="ac797_hostName" maxlength="31" placeholder="loading..."></dd></dl>',
  '<div class="formButtons">',
  '<button type="button" onclick="ac797SaveHostName()">Submit</button>',
  '</div>',
  '</fieldset>',
  '<p id="ac797_hostNameMsg"></p>',
  '</div></div>',
  '</div>',
  '<script>',
  'function ac797LoadHostName(){',
  "fetch('/model.json').then(function(r){return r.json();}).then(function(d){",
  'var v = d && d.router && d.router.hostName;',
  "var el = document.getElementById('ac797_hostName');",
  "if (el) el.value = v || '';",
  '});',
  '}',
  'function ac797PostHostName(val){',
  "return fetch('/model.json').then(function(r){return r.json();}).then(function(d){",
  "var token = (d && d.session && d.session.secToken) || '';",
  "var body = 'router.hostName=' + encodeURIComponent(val) + '&token=' + encodeURIComponent(token) + '&session.lang=en';",
  "return fetch('/Forms/config', {method:'POST', headers:{'Content-Type':'application/x-www-form-urlencoded'}, body: body});",
  '});',
  '}',
  'function ac797SaveHostName(){',
  "var val = document.getElementById('ac797_hostName').value;",
  'ac797PostHostName(val).then(function(r){',
  'if (r.ok) return r;',
  'return ac797PostHostName(val);', // the secToken can rotate between fetch and post; retry once with a fresh one
  '}).then(function(r){',
  "if (!r.ok) { throw new Error('HTTP ' + r.status); }",
  "document.getElementById('ac797_hostNameMsg').textContent = 'Saved.';",
  'ac797LoadHostName();',
  '}).catch(function(e){',
  "document.getElementById('ac797_hostNameMsg').textContent = 'Error: ' + e;",
  '});',
  '}',
  'ac797LoadHostName();',
  '</script>',
].join('\n');

const newContentB = contentB.slice(0, splitB) + panelHtml + contentB.slice(splitB);
console.log('Record B (panel):    old len', recB.len, '-> new len', newContentB.length);

if (newContentA.length > 0xffff || newContentB.length > 0xffff) {
  throw new Error('a patched record exceeds the 16-bit length field (65535 bytes)');
}

function makeRecord(opcode, contentStr) {
  const content = Buffer.from(contentStr, 'latin1');
  const header = Buffer.alloc(3);
  header[0] = opcode;
  header.writeUInt16LE(content.length, 1);
  return Buffer.concat([header, content]);
}

const out = Buffer.concat([
  buf.slice(0, recA.headerStart),
  makeRecord(1, newContentA),
  buf.slice(recA.contentEnd, recB.headerStart),
  makeRecord(1, newContentB),
  buf.slice(recB.contentEnd),
]);

fs.writeFileSync(outPath, out);
console.log('Original size:', buf.length, 'bytes. Patched size:', out.length, 'bytes.');
