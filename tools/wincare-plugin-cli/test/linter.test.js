const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const {
  validatePlugin,
  ID_REGEX,
  MAX_MANIFEST_BYTES,
  CANONICAL_MANIFEST,
  FALLBACK_MANIFEST
} = require('../src/linter/manifestLinter');

function makePluginDir() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'wincare-lint-'));
  return dir;
}

function writeManifest(dir, manifest, fileName = CANONICAL_MANIFEST) {
  fs.writeFileSync(path.join(dir, fileName), JSON.stringify(manifest, null, 2));
  return path.join(dir, fileName);
}

function baseManifest(overrides) {
  return Object.assign({
    id: 'com.example.plugin',
    name: 'Example',
    version: '1.0.0',
    author: 'Developer',
    category: 'Utilities',
    tools: [
      { id: 'com.example.plugin.run', title: 'Run', risk: 'ReadOnly', scriptPath: 'scripts/run.cmd' }
    ]
  }, overrides);
}

function touch(dir, relativePath, contents = '@echo off\r\n') {
  const full = path.join(dir, relativePath);
  fs.mkdirSync(path.dirname(full), { recursive: true });
  fs.writeFileSync(full, contents);
  return full;
}

test('a well-formed plugin validates', () => {
  const dir = makePluginDir();
  touch(dir, 'scripts/run.cmd');
  writeManifest(dir, baseManifest());
  const result = validatePlugin(dir);
  assert.deepStrictEqual(result.errors, []);
  assert.strictEqual(result.valid, true);
  assert.strictEqual(result.manifestPath, path.join(dir, CANONICAL_MANIFEST));
});

test('F-3: canonical risk "Mutating" is rejected', () => {
  const dir = makePluginDir();
  touch(dir, 'scripts/run.cmd');
  writeManifest(dir, baseManifest({ tools: [{ id: 'com.example.plugin.run', title: 'Run', risk: 'Mutating', scriptPath: 'scripts/run.cmd' }] }));
  const result = validatePlugin(dir);
  assert.strictEqual(result.valid, false);
  assert.ok(result.errors.some(e => e.includes('invalid risk "Mutating"')), result.errors.join('\n'));
});

test('F-3: canonical risk "Elevated" is rejected', () => {
  const dir = makePluginDir();
  touch(dir, 'scripts/run.cmd');
  writeManifest(dir, baseManifest({ tools: [{ id: 'com.example.plugin.run', title: 'Run', risk: 'Elevated', scriptPath: 'scripts/run.cmd' }] }));
  assert.strictEqual(validatePlugin(dir).valid, false);
});

test('F-3: legacy riskLevel "Mutating"/"Elevated" stay valid and warn about their mapping', () => {
  const dir = makePluginDir();
  touch(dir, 'scripts/run.cmd');
  writeManifest(dir, baseManifest({ tools: [{ id: 'com.example.plugin.run', title: 'Run', riskLevel: 'Mutating', scriptPath: 'scripts/run.cmd' }] }));
  const result = validatePlugin(dir);
  assert.strictEqual(result.valid, true);
  assert.ok(result.warnings.some(w => w.includes('read as risk "Moderate"')), result.warnings.join('\n'));

  const dir2 = makePluginDir();
  touch(dir2, 'scripts/run.cmd');
  writeManifest(dir2, baseManifest({ tools: [{ id: 'com.example.plugin.run', title: 'Run', riskLevel: 'Elevated', scriptPath: 'scripts/run.cmd' }] }));
  const result2 = validatePlugin(dir2);
  assert.strictEqual(result2.valid, true);
  assert.ok(result2.warnings.some(w => w.includes('read as risk "High"')), result2.warnings.join('\n'));
});

test('F-4: an ID longer than 128 characters is rejected', () => {
  const dir = makePluginDir();
  const longId = 'com.example.' + 'a'.repeat(125);
  assert.ok(longId.length > 128);
  writeManifest(dir, baseManifest({ id: longId }));
  const result = validatePlugin(dir);
  assert.strictEqual(result.valid, false);
  assert.ok(result.errors.some(e => e.includes('3 to 128 characters')), result.errors.join('\n'));
});

test('F-4: a two-segment ID stays invalid and a three-character ID is accepted', () => {
  assert.strictEqual(ID_REGEX.test('com'), false);
  assert.strictEqual(ID_REGEX.test('com.example'), true);
  assert.strictEqual(ID_REGEX.test('a.b'), true);
  assert.strictEqual(ID_REGEX.test('COM.EXAMPLE'), false);
  assert.strictEqual(ID_REGEX.test('com.example_extras'), false);
});

test('F-7: a manifest over 1 MiB is rejected', () => {
  const dir = makePluginDir();
  touch(dir, 'scripts/run.cmd');
  const manifest = baseManifest({ description: 'x'.repeat(MAX_MANIFEST_BYTES) });
  writeManifest(dir, manifest);
  const result = validatePlugin(dir);
  assert.strictEqual(result.valid, false);
  assert.ok(result.errors.some(e => e.includes('size limit')), result.errors.join('\n'));
});

test('F-7: plugin.json is accepted as a fallback filename with a warning', () => {
  const dir = makePluginDir();
  touch(dir, 'scripts/run.cmd');
  writeManifest(dir, baseManifest(), FALLBACK_MANIFEST);
  const result = validatePlugin(dir);
  assert.strictEqual(result.valid, true);
  assert.strictEqual(result.manifestPath, path.join(dir, FALLBACK_MANIFEST));
  assert.ok(result.warnings.some(w => w.includes('canonical')), result.warnings.join('\n'));
});

test('F-7: a missing manifest names both accepted filenames', () => {
  const dir = makePluginDir();
  const result = validatePlugin(dir);
  assert.strictEqual(result.valid, false);
  assert.ok(result.errors[0].includes(FALLBACK_MANIFEST), result.errors.join('\n'));
});

test('F-2: an Assembly manifest without the declared assembly fails', () => {
  const dir = makePluginDir();
  writeManifest(dir, baseManifest({
    entryType: 'Assembly',
    targetFramework: 'net8.0-windows10.0.19041.0',
    assemblyFileName: 'PluginAssembly.dll',
    tools: [{ id: 'com.example.plugin.run', title: 'Run', risk: 'ReadOnly' }]
  }));
  const result = validatePlugin(dir);
  assert.strictEqual(result.valid, false);
  assert.ok(result.errors.some(e => e.includes('assembly file does not exist')), result.errors.join('\n'));
});

test('F-2: an Assembly manifest with the assembly present passes', () => {
  const dir = makePluginDir();
  touch(dir, 'PluginAssembly.dll', Buffer.from([0x4d, 0x5a]));
  writeManifest(dir, baseManifest({
    entryType: 'Assembly',
    targetFramework: 'net8.0-windows10.0.19041.0',
    assemblyFileName: 'PluginAssembly.dll',
    assemblyEntry: 'PluginAssembly.dll',
    tools: [{ id: 'com.example.plugin.run', title: 'Run', risk: 'ReadOnly' }]
  }));
  assert.strictEqual(validatePlugin(dir).valid, true);
});

test('F-2: assemblyFileName that escapes the plugin root fails', () => {
  const dir = makePluginDir();
  writeManifest(dir, baseManifest({
    entryType: 'Assembly',
    targetFramework: 'net8.0-windows10.0.19041.0',
    assemblyFileName: '../outside.dll',
    tools: [{ id: 'com.example.plugin.run', title: 'Run', risk: 'ReadOnly' }]
  }));
  const result = validatePlugin(dir);
  assert.strictEqual(result.valid, false);
  assert.ok(result.errors.some(e => e.includes('assemblyFileName')), result.errors.join('\n'));
});

test('F-8: an unsupported script extension warns rather than failing', () => {
  const dir = makePluginDir();
  touch(dir, 'scripts/run.exe');
  writeManifest(dir, baseManifest({ tools: [{ id: 'com.example.plugin.run', title: 'Run', risk: 'ReadOnly', scriptPath: 'scripts/run.exe' }] }));
  const result = validatePlugin(dir);
  assert.strictEqual(result.valid, true);
  assert.ok(result.warnings.some(w => w.includes('directly as an executable')), result.warnings.join('\n'));
});

test('F-9: a bundled wincare-plugin.sig warns', () => {
  const dir = makePluginDir();
  touch(dir, 'scripts/run.cmd');
  touch(dir, 'wincare-plugin.sig', 'bogus');
  writeManifest(dir, baseManifest());
  const result = validatePlugin(dir);
  assert.ok(result.warnings.some(w => w.includes('not an independent trust assertion')), result.warnings.join('\n'));
});

test('F-9: an inline manifest signature property warns', () => {
  const dir = makePluginDir();
  touch(dir, 'scripts/run.cmd');
  writeManifest(dir, baseManifest({ signature: 'bogus-base64' }));
  const result = validatePlugin(dir);
  assert.ok(result.warnings.some(w => w.includes('inline manifest "signature"')), result.warnings.join('\n'));
});

test('scriptPath traversal and absolute paths still fail', () => {
  const dir = makePluginDir();
  writeManifest(dir, baseManifest({ tools: [{ id: 'com.example.plugin.run', title: 'Run', risk: 'ReadOnly', scriptPath: '../../evil.cmd' }] }));
  assert.ok(validatePlugin(dir).errors.some(e => e.includes('illegal path traversal')));

  const dir2 = makePluginDir();
  writeManifest(dir2, baseManifest({ tools: [{ id: 'com.example.plugin.run', title: 'Run', risk: 'ReadOnly', scriptPath: '/etc/evil.cmd' }] }));
  assert.ok(validatePlugin(dir2).errors.some(e => e.includes('absolute path')));
});
