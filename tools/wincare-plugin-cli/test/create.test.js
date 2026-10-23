const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { createPlugin, PLUGIN_NAME_REGEX, escapeCsString } = require('../src/commands/create');
const { ID_REGEX } = require('../src/linter/manifestLinter');

function makeOutDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'wincare-create-'));
}

function readManifest(dir) {
  return JSON.parse(fs.readFileSync(path.join(dir, 'wincare-plugin.json'), 'utf8'));
}

test('F-10: a name with batch or C# metacharacters is rejected', () => {
  const out = makeOutDir();
  assert.throws(() => createPlugin('x & calc', { outDir: path.join(out, 'a') }), /Invalid plugin name/);
  assert.throws(() => createPlugin('x"; code(); "', { outDir: path.join(out, 'b') }), /Invalid plugin name/);
  assert.throws(() => createPlugin('..', { outDir: path.join(out, 'c') }), /Invalid plugin name/);
  assert.throws(() => createPlugin('../../escape', { outDir: path.join(out, 'd') }), /Invalid plugin name/);
});

test('F-10: without --outDir the target directory stays inside the current directory', () => {
  const previousCwd = process.cwd();
  const sandbox = fs.mkdtempSync(path.join(os.tmpdir(), 'wincare-cwd-'));
  process.chdir(sandbox);
  try {
    // A validated name cannot contain separators, so every scaffold lands inside the cwd;
    // the confinement check is the guard that keeps that true if the name rule ever loosens.
    for (const name of ['inside_tool', 'nested name', 'a.b_c']) {
      const result = createPlugin(name, {});
      const relative = path.relative(sandbox, result.targetDir);
      assert.strictEqual(relative.startsWith('..' + path.sep), false);
      assert.strictEqual(path.isAbsolute(relative), false);
      assert.ok(fs.existsSync(result.targetDir));
    }
  } finally {
    process.chdir(previousCwd);
  }
});

test('F-10: an --outDir outside the current directory is honoured', () => {
  const previousCwd = process.cwd();
  const sandbox = fs.mkdtempSync(path.join(os.tmpdir(), 'wincare-cwd-'));
  const elsewhere = fs.mkdtempSync(path.join(os.tmpdir(), 'wincare-elsewhere-'));
  process.chdir(sandbox);
  try {
    const result = createPlugin('external', { outDir: path.join(elsewhere, 'external') });
    assert.strictEqual(path.resolve(result.targetDir), path.resolve(elsewhere, 'external'));
  } finally {
    process.chdir(previousCwd);
  }
});

test('F-10: author and description are escaped before embedding in generated C#', () => {
  const out = makeOutDir();
  const dir = path.join(out, 'cs');
  createPlugin('cstool', {
    template: 'csharp-plugin',
    outDir: dir,
    author: 'A"; System.Diagnostics.Process.Start("calc"); //',
    description: 'quote " backslash \\ newline\r\nend'
  });
  const cs = fs.readFileSync(path.join(dir, 'PluginEntryPoint.cs'), 'utf8');
  // Escaping neutralises the characters syntactically; the text is still present as data, so
  // the check that matters is the exact escaped literal.
  assert.ok(
    cs.includes('public string Author => "A\\"; System.Diagnostics.Process.Start(\\"calc\\"); //";'),
    cs
  );
  assert.ok(cs.includes('public string Description => "quote \\" backslash \\\\ newline end";'), cs);
});

test('F-10: the generated .cmd line carries no batch operators', () => {
  const out = makeOutDir();
  const dir = path.join(out, 'jp');
  createPlugin('safetool', { outDir: dir });
  const cmd = fs.readFileSync(path.join(dir, 'scripts', 'clean_temp.cmd'), 'utf8');
  assert.ok(!/[&|<>^%!()"]/.test(cmd.replace(/@echo off/, '')), cmd);
});

test('F-4: the scaffolded id validates against the linter rule', () => {
  const out = makeOutDir();
  for (const name of ['test_cleaner', 'test_assembly_tool', 'repeat_pack', 'a']) {
    const result = createPlugin(name, { outDir: path.join(out, name) });
    assert.strictEqual(ID_REGEX.test(result.id), true, `${result.id} should validate`);
  }
});

test('F-4: the longest accepted name still yields a usable id', () => {
  const out = makeOutDir();
  // The name cap (64) keeps the generated id inside the 128-character installer limit, so the
  // fallback id is only a guard; exercise the boundary the linter actually enforces.
  const longName = 'a'.repeat(64);
  const result = createPlugin(longName, { outDir: path.join(out, 'long') });
  assert.strictEqual(result.id, 'com.community.' + 'a'.repeat(64));
  assert.strictEqual(ID_REGEX.test(result.id), true);
  // The guard's target: the trailing-dot form the old scaffolder could emit is not a valid id.
  assert.strictEqual(ID_REGEX.test('com.community.'), false);
});

test('the C# template scaffolds a manifest that declares its assembly', () => {
  const out = makeOutDir();
  const dir = path.join(out, 'cs');
  const result = createPlugin('assemblertool', { template: 'csharp-plugin', outDir: dir });
  const manifest = readManifest(dir);
  assert.strictEqual(manifest.entryType, 'Assembly');
  assert.strictEqual(manifest.assemblyFileName, 'PluginAssembly.dll');
  assert.strictEqual(manifest.id, result.id);
  assert.ok(!('executorType' in manifest.tools[0]), 'the runtime reads no executor type');
});

test('the json-pack template scaffolds a script plugin with a runnable script', () => {
  const out = makeOutDir();
  const dir = path.join(out, 'jp');
  createPlugin('scripttool', { outDir: dir });
  const manifest = readManifest(dir);
  assert.strictEqual(manifest.entryType, 'Manifest');
  assert.strictEqual(manifest.tools[0].scriptPath, 'scripts/clean_temp.cmd');
  assert.ok(fs.existsSync(path.join(dir, manifest.tools[0].scriptPath)));
});

test('escapeCsString neutralises quotes, backslashes and control characters', () => {
  assert.strictEqual(escapeCsString('a"b'), 'a\\"b');
  assert.strictEqual(escapeCsString('a\\b'), 'a\\\\b');
  assert.strictEqual(escapeCsString('a\r\nb'), 'a b');
  assert.strictEqual(escapeCsString(undefined), '');
});

test('PLUGIN_NAME_REGEX allows identifier names and rejects metacharacters', () => {
  for (const ok of ['tool', 'my_tool', 'My Tool 2', 'a.b_c']) {
    assert.strictEqual(PLUGIN_NAME_REGEX.test(ok), true, ok);
  }
  for (const bad of ['x & calc', 'a;b', 'a|b', 'a<b', 'a"b', "../x", 'a\nb', '']) {
    assert.strictEqual(PLUGIN_NAME_REGEX.test(bad), false, bad);
  }
});
