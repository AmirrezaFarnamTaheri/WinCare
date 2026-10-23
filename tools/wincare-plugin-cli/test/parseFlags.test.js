const test = require('node:test');
const assert = require('node:assert');
const { parseFlags } = require('../src/util/parseFlags');

test('parseFlags collects positionals and values', () => {
  const result = parseFlags(['create', 'my-plugin', '--template', 'csharp-plugin', '--outDir', '/tmp/x'], {
    template: {},
    outDir: {}
  });
  assert.deepStrictEqual(result.positionals, ['create', 'my-plugin']);
  assert.deepStrictEqual(result.flags, { template: 'csharp-plugin', outDir: '/tmp/x' });
});

test('parseFlags supports inline --flag=value', () => {
  const result = parseFlags(['--out=result.zip'], { out: { alias: 'output' } });
  assert.deepStrictEqual(result.flags, { output: 'result.zip' });
});

test('parseFlags maps an alias to its target name', () => {
  const result = parseFlags(['--out', 'result.zip'], { out: { alias: 'output' }, output: {} });
  assert.deepStrictEqual(result.flags, { output: 'result.zip' });
});

test('parseFlags treats a boolean flag as true without a value', () => {
  const result = parseFlags(['--verbose', 'file'], { verbose: { boolean: true } });
  assert.deepStrictEqual(result.positionals, ['file']);
  assert.deepStrictEqual(result.flags, { verbose: true });
});

test('parseFlags rejects an unknown flag', () => {
  assert.throws(() => parseFlags(['--nope', 'x'], {}), /Unknown option "--nope"/);
});

test('parseFlags rejects a flag missing its value', () => {
  assert.throws(() => parseFlags(['--out'], { out: {} }), /Option "--out" requires a value/);
});

test('parseFlags rejects a value that begins with a dash', () => {
  assert.throws(() => parseFlags(['--out', '--template'], { out: {}, template: {} }), /must not be given a value that begins with "-"/);
});

test('parseFlags stops flag parsing after a bare --', () => {
  const result = parseFlags(['--', '--not-a-flag'], {});
  assert.deepStrictEqual(result.positionals, ['--not-a-flag']);
});

test('parseFlags keeps tokens that only look like flags', () => {
  const result = parseFlags(['-'], {});
  assert.deepStrictEqual(result.positionals, ['-']);
});

test('parseFlags accepts an empty argument list', () => {
  assert.deepStrictEqual(parseFlags([], {}), { positionals: [], flags: {} });
});
