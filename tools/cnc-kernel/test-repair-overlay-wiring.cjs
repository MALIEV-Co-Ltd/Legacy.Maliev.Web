const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.mkdtempSync(path.join(os.tmpdir(), 'cnc-overlay-wiring-'));
try {
  const destination = path.join(source, 'occt-import-js', 'src');
  fs.mkdirSync(destination, { recursive: true });
  // Repair helpers depend on headers supplied by the preceding real overlays.
  // Exercise their actual copy blocks, not a duplicated test-only file list.
  const base = fs.readFileSync(path.join(__dirname, 'apply-overlay.cjs'), 'utf8');
  const baseStart = base.indexOf("fs.copyFileSync(path.join(__dirname, 'kernel-face.hpp')");
  const baseEnd = base.indexOf('child.execFileSync(process.execPath', baseStart);
  assert(baseStart >= 0 && baseEnd > baseStart, 'actual base overlay copy block exists');
  vm.runInNewContext(base.slice(baseStart, baseEnd), { fs, path, source, __dirname });
  const document = fs.readFileSync(path.join(__dirname, 'apply-document-overlay.cjs'), 'utf8');
  const documentStart = document.indexOf("for (const file of ['kernel-document.hpp'");
  assert(documentStart >= 0, 'actual document overlay copy block exists');
  vm.runInNewContext(document.slice(documentStart), { fs, path, source, __dirname });
  const overlay = fs.readFileSync(path.join(__dirname, 'apply-repair-overlay.cjs'), 'utf8');
  const start = overlay.indexOf("fs.copyFileSync(path.join(__dirname,'kernel-native-interpretation-export.hpp')");
  assert(start >= 0, 'actual final overlay copy block exists');
  // Execute the actual copy block in a fresh disposable destination, without
  // rerunning source hooks against an already-patched live OCCT checkout.
  vm.runInNewContext(overlay.slice(start), { fs, path, source, __dirname });
  assert(fs.existsSync(path.join(destination, 'kernel-native-interpretation-audit.hpp')),
    'fresh overlay must copy the required native audit header');
  let checks = 0;
  for (const name of fs.readdirSync(destination)) {
    assert.deepEqual(fs.readFileSync(path.join(destination, name)),
      fs.readFileSync(path.join(__dirname, name)), 'exact copied bytes: ' + name);
    ++checks;
    for (const match of fs.readFileSync(path.join(destination, name), 'utf8')
      .matchAll(/#include\s+"(kernel-[^"/]+\.hpp)"/g)) {
      assert(fs.existsSync(path.join(destination, match[1])),
        name + ' requires copied dependency ' + match[1]);
      ++checks;
    }
  }
  console.log('PASS: ' + checks + ' fresh overlay header wiring checks');
} finally {
  assert(path.dirname(source) === path.resolve(os.tmpdir()));
  assert(path.basename(source).startsWith('cnc-overlay-wiring-'));
  fs.rmSync(source, { recursive: true });
}
