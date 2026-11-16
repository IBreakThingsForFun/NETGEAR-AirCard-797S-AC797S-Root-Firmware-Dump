#!/usr/bin/env node
// Adds three new screens to the LCD's index.html.tmpl + script.js, reachable by the
// device's own physical Next/Prev buttons alongside dashboard/messages/alerts/wifi/
// wps/about: a "Hello" screen, a terminal-styled one showing the root exploit's
// payoff, and a "SYSTEM" screen with real facts about this unit (kernel build,
// RAM, wlan0/wlan1 MACs) that no stock screen shows. The MAC/kernel values below
// were read live off this device with `AC797.exe sh 'uname -a; cat
// /sys/class/net/wlan0/address; cat /sys/class/net/wlan1/address'` — update SCREENS
// below if you regenerate this against a different unit.
//
// Two independent patches:
//
// 1. index.html.tmpl is the same length-prefixed binary record format as the admin
//    portal's template (see tools/patch_general2.js for the format). We splice three
//    new literal <div class="fade_in_module custom"> panels into the existing literal
//    record that already holds the "about" panel's closing tags, right before the
//    next panel ("wifi_dual_band_module") begins. Panel content is plain text only
//    (no multi-locale `?text en=...&es_US=...` directive — that's a different,
//    unreverse-engineered opcode — so this only ever shows English).
//
// 2. script.js drives screen navigation with a real finite-state-machine (class `la`,
//    minified). Every feature module (wifi/wps/sms/dataUsage/...) registers its own
//    screen and re-sets the *entire* top-level Next/Prev cycle each time it loads —
//    but there's exactly one module, "navigationStructureUI" (registered dead last in
//    the file), whose only job is to set the final, authoritative cycle order after
//    all the others have run. We patch that one module: register each new screen's
//    state on the shared root FSM (`I(a.b(),id,{...})`, same shape as the
//    dynamically-added "wifi"/"wps"/"sms" states) and insert all three into its cycle
//    array, right before "about".
//
// Usage: node patch_lcd_screen.js <src lcd dir with index.html.tmpl+script.js> <out dir>
//
// Deploy with `AC797.exe pushbin <file> /tmp/x.bin`, then as root:
//   mount -o remount,rw /mnt/hdata
//   cp /tmp/x.bin /mnt/hdata/lcd/index.html.tmpl   (or .../lcd/js/script.js)
//   mount -o remount,ro /mnt/hdata
// The native `Gui` process (DirectFB+WebKit, renders the LCD) caches nothing on
// disk the way NetgearWebApp does for the admin portal, but it DOES hold the already-
// loaded page in memory — kill its PID (`ps | grep ' Gui '`) to force a reload of
// index.html.tmpl/script.js. Unlike the web portal (whose watchdog is the separate
// restartNetgearWebApp), the LCD has its own supervisor, `restartGui` (`ps | grep
// restartGui`) — it respawns Gui with the same command line within a couple of
// seconds, no manual relaunch needed:
//   Gui --dfb:bg-image=/mnt/hdata/lcd/images/guibg.png file:///mnt/hdata/lcd/intro.html?http://127.0.0.1/lcd/index.html

const fs = require('fs');
const path = require('path');

const [, , srcDir, outDir] = process.argv;
if (!srcDir || !outDir) {
  console.error('Usage: node patch_lcd_screen.js <src lcd dir> <out dir>');
  process.exit(1);
}
fs.mkdirSync(outDir, { recursive: true });

// Real, unchanging facts about this specific unit (kernel build + wlan MACs don't
// change across reboots, unlike uptime/temp/clients — baking them in stays honest).
//
// SYSTEM (custom3) also demonstrates the LCD's physical button beyond a plain
// tap: holding it (~1s) flips between two info pages on the SAME screen. The
// button is a single physical key whose own firmware (script.js's buttonUI
// module) already distinguishes a quick tap (dispatched as "NextEvent" on
// release) from a hold — it fires a "holdInterval" event once per second held,
// which the active screen's own FSM state receives as a hold_interval(seconds)
// callback if it defines one (exactly how the stock language/APN-selector and
// WPS-cancel screens already drive their own hold-to-confirm progress bars).
// We hook that same callback, toggling which <dl> is visible. This is safely
// clear of the two *global*, button-wide behaviors layered on top of every
// screen's own hold_interval by other, unrelated modules: a real "Power"
// action fires at the 3s mark, and at 4s the LCD unconditionally jumps to the
// shutdown/factory-reset confirmation regardless of what screen was showing
// (pre-existing stock behavior, not something this patch touches) — so our own
// toggle fires on the very first tick (1s) to stay well clear of both.
const SCREENS = [
  {
    id: 'custom', title: 'AC797',
    bodyHtml: '\t<p id="custom_text">Hello from AC797</p>',
  },
  {
    id: 'custom2', title: 'ROOT',
    bodyHtml: '\t<pre id="ac797_term" style="background:#000;color:#0f0;font-family:monospace;font-size:11px;line-height:1.4;padding:8px;margin:6px;border-radius:4px;white-space:pre-wrap;">$ telnet 192.168.1.1\n/ # id\nuid=0(root) gid=0(root)\n\nROOT ACCESS: GRANTED</pre>',
  },
  {
    id: 'custom3', title: 'SYSTEM',
    bodyHtml: [
      '\t<dl id="ac797_sys_p1" class="about about_small_font">',
      '\t\t<dt>Kernel:</dt><dd>3.10.49-svn5662</dd>',
      '\t\t<dt>Built:</dt><dd>2019-11-21</dd>',
      '\t\t<dt>RAM:</dt><dd>150 MB</dd>',
      '\t</dl>',
      '\t<dl id="ac797_sys_p2" class="about about_small_font" style="display:none;">',
      '\t\t<dt>SoC:</dt><dd>Qualcomm MDM9640</dd>',
      '\t\t<dt>wlan0:</dt><dd>44:a5:6e:dc:ac:e7</dd>',
      '\t\t<dt>wlan1:</dt><dd>46:a5:6e:dc:ac:e7</dd>',
      '\t</dl>',
      '\t<p style="font-size:9px;color:#999;margin:4px 8px;">Hold button for more</p>',
    ].join('\n'),
    // Fires once per second held, for as long as the button stays down (see the
    // big comment above for why this reacts on the very first tick only). Sets
    // the `held` flag the registration wrapper below declares, so the auto-
    // generated `next` override (suppressNextAfterHold) can swallow the
    // "NextEvent" that the button's keyup handler unconditionally fires on
    // *every* release, held or not — without that, releasing after a hold
    // both flips the page AND advances to the next screen.
    extraFsm: 'hold_interval:function(s){held=!0;if(1>s)return!0;' +
      'var p1=document.getElementById("ac797_sys_p1"),p2=document.getElementById("ac797_sys_p2");' +
      'if(p1&&p2){var showing2="none"!==p2.style.display;p1.style.display=showing2?"":"none";p2.style.display=showing2?"none":"";}' +
      'return!0}',
    suppressNextAfterHold: true,
    // Reset to page 1 every time the screen is (re)entered, so a long-press on a
    // previous visit doesn't leave it stuck on page 2.
    extraEnter: 'var p1=document.getElementById("ac797_sys_p1"),p2=document.getElementById("ac797_sys_p2");' +
      'if(p1&&p2){p1.style.display="";p2.style.display="none";}',
  },
];

// ---------- 1. index.html.tmpl ----------
{
  const buf = fs.readFileSync(path.join(srcDir, 'index.html.tmpl'));

  function record(headerStart) {
    const opcode = buf[headerStart];
    const len = buf.readUInt16LE(headerStart + 1);
    return { headerStart, opcode, len, contentStart: headerStart + 3, contentEnd: headerStart + 3 + len };
  }

  const needle = '</dl>\n</div>\n\n\n\t</div>\n\t<div class="fade_in_module six" tabindex="0">';
  const idx = buf.indexOf(Buffer.from(needle, 'latin1'));
  if (idx < 0) throw new Error('anchor text not found — LCD template layout may have changed');

  let headerStart = -1;
  for (let back = 1; back <= 2000; back++) {
    const hs = idx - back;
    if (buf[hs] === 1) {
      const r = record(hs);
      if (r.contentStart <= idx && r.contentEnd > idx) { headerStart = hs; break; }
    }
  }
  if (headerStart < 0) throw new Error('enclosing literal record not found');
  const rec = record(headerStart);
  if (rec.opcode !== 1) throw new Error('record opcode mismatch: ' + rec.opcode);

  const content = buf.slice(rec.contentStart, rec.contentEnd).toString('latin1');
  const splitMarker = '</dl>\n</div>\n\n\n\t</div>';
  const splitAt = content.indexOf(splitMarker) + splitMarker.length;
  if (splitAt < splitMarker.length) throw new Error('split marker not found inside record content');

  const panelHtml = SCREENS.map(s => [
    '',
    // The LCD's CSS only defines z-index rules for a fixed set of fade_in_module
    // class names ("zero".."twentytwo" — see styles.css), all 23 of which are
    // already used by existing panels. A panel with no matching class gets no
    // z-index at all and stays invisible even when "active" (panelshow/opacity:1),
    // since .fade_in_module is position:absolute and the other panels still stack
    // on top of it. Reusing "twentytwo" is safe: only one panel has the
    // "panelshow" class (opacity:1) at a time, so sharing a stacking layer with a
    // panel that is not visible at the same time has no effect.
    `\t<div class="fade_in_module twentytwo" tabindex="0">`,
    `\t\t<h3 id="${s.id}_screen">`,
    `\t<span class="head_title_text">${s.title}</span>`,
    '</h3>',
    `<div id="module_${s.id}_container">`,
    s.bodyHtml,
    '</div>',
    '\t</div>',
  ].join('\n')).join('\n');

  const newContent = content.slice(0, splitAt) + panelHtml + content.slice(splitAt);
  if (newContent.length > 0xffff) throw new Error('record too large for 16-bit length field');
  console.log('index.html.tmpl: record at', headerStart, 'old len', rec.len, '-> new len', newContent.length);

  function makeRecord(opcode, contentStr) {
    const c = Buffer.from(contentStr, 'latin1');
    const header = Buffer.alloc(3);
    header[0] = opcode;
    header.writeUInt16LE(c.length, 1);
    return Buffer.concat([header, c]);
  }

  const out = Buffer.concat([
    buf.slice(0, rec.headerStart),
    makeRecord(1, newContent),
    buf.slice(rec.contentEnd),
  ]);
  fs.writeFileSync(path.join(outDir, 'index.html.tmpl'), out);
  console.log('index.html.tmpl: original size', buf.length, '-> patched size', out.length);
}

// ---------- 2. script.js ----------
{
  const src = fs.readFileSync(path.join(srcDir, 'script.js'), 'utf8');

  const needle = 'return{name:"navigationStructureUI",e:function(b,c){a=c;O(a.b(),"dashboard sms alerts wifi wps dataUsage about".split(" "));O(a.b().b("wifi"),["guest_credentials"])}';
  const idx = src.indexOf(needle);
  if (idx < 0) throw new Error('navigationStructureUI module not found — script.js layout may have changed');

  // Each screen's registration gets its own IIFE so a `held` flag can be shared
  // privately between its hold_interval and next handlers: the button's keyup
  // handler unconditionally fires "NextEvent" on every release (tap or hold),
  // so a screen that reacts to holding also needs to swallow the one Next that
  // follows release — otherwise it both reacts to the hold AND advances to the
  // next screen. suppressNextAfterHold wires up that `next` override; screens
  // that don't set it get an unused (harmless) `held` var and the framework's
  // normal default Next/Prev cycling.
  const registrations = SCREENS.map(s => {
    const next = s.suppressNextAfterHold
      ? 'next:function(){if(held){held=!1;return!0}return!1},'
      : '';
    return `(function(){var held=!1;I(a.b(),"${s.id}",{name:"${s.id}",` +
      `d:function(){a.l("${s.id}_screen");held=!1;${s.extraEnter || ''}},` +
      next +
      (s.extraFsm || '') +
      '});})();';
  }).join('');
  const cycle = `dashboard sms alerts wifi wps dataUsage ${SCREENS.map(s => s.id).join(' ')} about`;

  const replacement =
    'return{name:"navigationStructureUI",e:function(b,c){a=c;' +
    registrations +
    `O(a.b(),"${cycle}".split(" "));` +
    'O(a.b().b("wifi"),["guest_credentials"])}';

  const out = src.slice(0, idx) + replacement + src.slice(idx + needle.length);
  fs.writeFileSync(path.join(outDir, 'script.js'), out, 'utf8');
  console.log('script.js: patched navigationStructureUI module (', needle.length, '->', replacement.length, 'chars)');
}
