const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const zlib = require('zlib');
const { DeterministicZipWriter, packPlugin } = require('../src/packager/zipBuilder');

function makePluginDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'wincare-zip-'));
}

/**
 * Minimal central-directory reader: enough to assert which entry names an archive carries
 * without depending on a third-party unzip implementation.
 */
function entryNames(archive) {
  const buffer = Buffer.isBuffer(archive) ? archive : fs.readFileSync(archive);
  const names = [];
  let offset = buffer.length - 22;
  assert.strictEqual(buffer.readUInt32LE(offset), 0x06054b50, 'EOCD signature');
  const cdOffset = buffer.readUInt32LE(offset + 16);
  let cursor = cdOffset;
  while (cursor < buffer.length && buffer.readUInt32LE(cursor) === 0x02014b50) {
    const nameLength = buffer.readUInt16LE(cursor + 28);
    names.push(buffer.subarray(cursor + 46, cursor + 46 + nameLength).toString('utf8'));
    cursor += 46 + nameLength + buffer.readUInt16LE(cursor + 30) + buffer.readUInt16LE(cursor + 32);
  }
  return names;
}

test('F-5: addFile rejects a traversal entry instead of rewriting it', () => {
  const writer = new DeterministicZipWriter();
  assert.throws(() => writer.addFile('../../evil.txt', 'data'), /unsafe archive entry name/);
  assert.throws(() => writer.addFile('sub/../../evil.txt', 'data'), /unsafe archive entry name/);
});

test('F-5: addFile rejects absolute and Windows-rooted entry names', () => {
  const writer = new DeterministicZipWriter();
  assert.throws(() => writer.addFile('/etc/evil.txt', 'data'), /unsafe archive entry name/);
  assert.throws(() => writer.addFile('C:\\Windows\\evil.txt', 'data'), /unsafe archive entry name/);
  assert.throws(() => writer.addFile('\\\\server\\share\\evil.txt', 'data'), /unsafe archive entry name/);
});

test('F-5: addFile keeps normalising separators for legitimate names', () => {
  const writer = new DeterministicZipWriter();
  writer.addFile('scripts\\nested\\run.cmd', 'data');
  assert.deepStrictEqual(entryNames(writer.build()), ['scripts/nested/run.cmd']);
});

test('F-5: a rejected addFile leaves the writer empty', () => {
  const writer = new DeterministicZipWriter();
  writer.addFile('ok.txt', 'data');
  assert.throws(() => writer.addFile('../bad.txt', 'data'));
  assert.deepStrictEqual(entryNames(writer.build()), ['ok.txt']);
});

test('the fixed timestamp is the real zip epoch, 1980-01-01 00:00:00', () => {
  const writer = new DeterministicZipWriter();
  writer.addFile('a.txt', 'data');
  const archive = writer.build();
  const dosTime = archive.readUInt16LE(10);
  const dosDate = archive.readUInt16LE(12);
  assert.strictEqual(dosTime, 0x0000, 'MS-DOS time must encode 00:00:00');
  // year 0 since 1980 | month 1 | day 1
  assert.strictEqual(dosDate, (0 << 9) | (1 << 5) | 1, 'MS-DOS date must encode 1980-01-01');
});

test('entries stay sorted and deflated, and round-trip through zlib', () => {
  const writer = new DeterministicZipWriter();
  writer.addFile('b.txt', 'b-data');
  writer.addFile('a.txt', 'a-data');
  const archive = writer.build();
  assert.deepStrictEqual(entryNames(archive), ['a.txt', 'b.txt']);
  // Raw inflate consumes only its own stream and ignores the central directory that follows.
  assert.strictEqual(zlib.inflateRawSync(archive.subarray(30 + 'a.txt'.length)).toString(), 'a-data');
});

test('F-6: a failed pack leaves no archive and no temp file behind', () => {
  const dir = makePluginDir();
  for (let i = 0; i < 501; i++) {
    fs.writeFileSync(path.join(dir, `${i}.txt`), 'x');
  }
  const output = path.join(dir, 'plugin.wincare-plugin');
  assert.throws(() => packPlugin(dir, output), /installer entry/);
  assert.strictEqual(fs.existsSync(output), false);
  assert.strictEqual(fs.existsSync(output + '.tmp'), false);
});

test('F-6: a successful pack writes the archive atomically and leaves no temp file', () => {
  const dir = makePluginDir();
  fs.writeFileSync(path.join(dir, 'wincare-plugin.json'), '{}');
  const output = path.join(dir, 'plugin.wincare-plugin');
  const result = packPlugin(dir, output);
  assert.strictEqual(result.success, true);
  assert.strictEqual(fs.existsSync(output), true);
  assert.strictEqual(fs.existsSync(output + '.tmp'), false);
});

test('a repeat pack excludes the archive and its trust sidecars', () => {
  const dir = makePluginDir();
  fs.writeFileSync(path.join(dir, 'wincare-plugin.json'), '{}');
  const output = path.join(dir, 'plugin.wincare-plugin');
  packPlugin(dir, output);
  fs.writeFileSync(output + '.sha256', 'digest\n');
  fs.writeFileSync(output + '.sig.json', '{"signature":"x"}\n');
  const result = packPlugin(dir, output);
  assert.deepStrictEqual(entryNames(result.outputPath), ['wincare-plugin.json']);
});
