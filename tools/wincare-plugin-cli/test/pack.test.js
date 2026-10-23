const test = require('node:test');
const assert = require('node:assert');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { createPlugin } = require('../src/commands/create');
const { runPack, signManifest, derivePublisherId, sha256Hex } = require('../src/commands/pack');
const { validatePlugin } = require('../src/linter/manifestLinter');

function makeTempDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'wincare-pack-'));
}

function scaffold(dir, name = 'signedtool') {
  const pluginDir = path.join(dir, name);
  createPlugin(name, { outDir: pluginDir });
  return pluginDir;
}

/**
 * Verifies a manifest signature exactly the way the consumer does
 * (PluginAdmissionTrustStore.VerifyManifestSignature): RSA PKCS#1 v1.5 over SHA-256, or
 * ECDSA over SHA-256, over the raw manifest bytes.
 */
function verifyLikeConsumer(rawBytes, signatureBase64, publicKeyPem) {
  const signature = Buffer.from(signatureBase64, 'base64');
  try {
    // RSA: PKCS#1 v1.5 over SHA-256, as in VerifyManifestSignature's
    // rsa.VerifyData(..., HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).
    const rsa = { key: crypto.createPublicKey(publicKeyPem), padding: crypto.constants.RSA_PKCS1_PADDING };
    return crypto.verify('sha256', rawBytes, rsa, signature);
  } catch (_err) {
    // ECDSA over SHA-256 for an EC key.
    return crypto.verify('sha256', rawBytes, crypto.createPublicKey(publicKeyPem), signature);
  }
}

test('F-6: a packaging failure returns the documented failure contract instead of throwing', () => {
  const root = makeTempDir();
  const pluginDir = path.join(root, 'plugin');
  fs.mkdirSync(pluginDir, { recursive: true });
  // No manifest at all -> validation fails first, exercising the same contract.
  const result = runPack(pluginDir, {});
  assert.strictEqual(result.success, false);
  assert.ok(result.errors.length > 0);
});

test('F-6: a pack whose archive cannot be written reports failure and leaves no partial file', () => {
  const root = makeTempDir();
  const pluginDir = scaffold(root, 'unwritable');
  const output = path.join(root, 'missing-dir', 'plugin.wincare-plugin');
  const result = runPack(pluginDir, { output });
  assert.strictEqual(result.success, false);
  assert.strictEqual(result.errors.length, 1);
  assert.strictEqual(fs.existsSync(output), false);
});

test('F-11: --output chooses the archive path', () => {
  const root = makeTempDir();
  const pluginDir = scaffold(root, 'outflag');
  const output = path.join(root, 'custom-name.wincare-plugin');
  const result = runPack(pluginDir, { output });
  assert.strictEqual(result.success, true);
  assert.strictEqual(result.outputPath, output);
  assert.strictEqual(fs.existsSync(output), true);
});

test('F-1: an unsigned pack warns and emits no trust metadata', () => {
  const root = makeTempDir();
  const pluginDir = scaffold(root, 'unsigned');
  const result = runPack(pluginDir, {});
  assert.strictEqual(result.success, true);
  assert.strictEqual(result.signed, undefined);
  assert.strictEqual(fs.existsSync(result.outputPath + '.sig.json'), false);
  assert.strictEqual(fs.existsSync(result.outputPath + '.sha256'), false);
});

test('F-1: --key signs the exact raw manifest bytes and emits the catalog record', () => {
  const root = makeTempDir();
  const pluginDir = scaffold(root, 'signed');
  const { privateKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
  const keyPath = path.join(root, 'publisher.pem');
  fs.writeFileSync(keyPath, privateKey.export({ type: 'pkcs8', format: 'pem' }));

  const result = runPack(pluginDir, { key: keyPath });
  assert.strictEqual(result.success, true);
  assert.strictEqual(result.signed, true);

  const trust = JSON.parse(fs.readFileSync(result.trustPath, 'utf8'));
  assert.strictEqual(trust.pluginId, 'com.community.signed');
  assert.strictEqual(trust.publisherId, derivePublisherId(fs.readFileSync(keyPath, 'utf8')));
  assert.strictEqual(trust.archiveSha256, sha256Hex(fs.readFileSync(result.outputPath)));

  const rawManifestBytes = fs.readFileSync(path.join(pluginDir, 'wincare-plugin.json'));
  assert.strictEqual(trust.manifestSha256, sha256Hex(rawManifestBytes));
  assert.strictEqual(
    verifyLikeConsumer(rawManifestBytes, trust.publisherSignature, trust.publisherPublicKeyPem),
    true,
    'the catalog must be able to verify the emitted signature over the raw manifest bytes'
  );
});

test('F-1: the signature is bound to the raw bytes, not to re-serialized JSON', () => {
  const root = makeTempDir();
  const pluginDir = path.join(root, 'respaced');
  createPlugin('respaced', { outDir: pluginDir });
  const manifestPath = path.join(pluginDir, 'wincare-plugin.json');
  const original = fs.readFileSync(manifestPath, 'utf8');

  const { privateKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
  const keyPath = path.join(root, 'publisher.pem');
  fs.writeFileSync(keyPath, privateKey.export({ type: 'pkcs8', format: 'pem' }));

  const result = runPack(pluginDir, { key: keyPath });
  const trust = JSON.parse(fs.readFileSync(result.trustPath, 'utf8'));

  // Same JSON document, different byte layout: the signature must not carry across.
  fs.writeFileSync(manifestPath, JSON.stringify(JSON.parse(original), null, 4));
  const respacedBytes = fs.readFileSync(manifestPath);
  assert.strictEqual(verifyLikeConsumer(respacedBytes, trust.publisherSignature, trust.publisherPublicKeyPem), false);
});

test('F-1: an optional UTF-8 BOM stays inside the signed span', () => {
  const root = makeTempDir();
  const pluginDir = path.join(root, 'bommed');
  createPlugin('bommed', { outDir: pluginDir });
  const manifestPath = path.join(pluginDir, 'wincare-plugin.json');
  const bom = Buffer.from([0xEF, 0xBB, 0xBF]);
  fs.writeFileSync(manifestPath, Buffer.concat([bom, fs.readFileSync(manifestPath)]));

  const { privateKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
  const keyPath = path.join(root, 'publisher.pem');
  fs.writeFileSync(keyPath, privateKey.export({ type: 'pkcs8', format: 'pem' }));

  const result = runPack(pluginDir, { key: keyPath });
  assert.strictEqual(result.success, true);
  const trust = JSON.parse(fs.readFileSync(result.trustPath, 'utf8'));

  const rawWithBom = fs.readFileSync(manifestPath);
  const rawWithoutBom = rawWithBom.subarray(3);
  assert.strictEqual(verifyLikeConsumer(rawWithBom, trust.publisherSignature, trust.publisherPublicKeyPem), true);
  assert.strictEqual(verifyLikeConsumer(rawWithoutBom, trust.publisherSignature, trust.publisherPublicKeyPem), false);
  // The manifest still decodes as JSON for the caller.
  assert.strictEqual(validatePlugin(pluginDir).valid, true);
});

test('F-1: an EC key signs ECDSA-SHA-256 over the same raw bytes', () => {
  const root = makeTempDir();
  const pluginDir = scaffold(root, 'ecsigned');
  const { privateKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' });
  const keyPath = path.join(root, 'publisher-ec.pem');
  fs.writeFileSync(keyPath, privateKey.export({ type: 'pkcs8', format: 'pem' }));

  const result = runPack(pluginDir, { key: keyPath });
  assert.strictEqual(result.success, true);
  const trust = JSON.parse(fs.readFileSync(result.trustPath, 'utf8'));
  const rawManifestBytes = fs.readFileSync(path.join(pluginDir, 'wincare-plugin.json'));
  assert.strictEqual(verifyLikeConsumer(rawManifestBytes, trust.publisherSignature, trust.publisherPublicKeyPem), true);
});

test('F-1: an unreadable key reports failure without touching the archive', () => {
  const root = makeTempDir();
  const pluginDir = scaffold(root, 'badkey');
  const result = runPack(pluginDir, { key: path.join(root, 'does-not-exist.pem') });
  assert.strictEqual(result.success, false);
  assert.ok(result.errors[0].includes('publisher key'), result.errors.join('\n'));
  assert.strictEqual(fs.existsSync(path.join(root, 'does-not-exist.pem.sig.json')), false);
});

test('F-1: a malformed key is reported clearly', () => {
  const root = makeTempDir();
  const pluginDir = scaffold(root, 'malformed');
  const keyPath = path.join(root, 'garbage.pem');
  fs.writeFileSync(keyPath, 'not a key\n');
  const result = runPack(pluginDir, { key: keyPath });
  assert.strictEqual(result.success, false);
  assert.ok(result.errors[0].includes('not a usable PEM private key'), result.errors.join('\n'));
});

test('F-1: a signing failure surfaces through the failure contract instead of throwing', () => {
  const root = makeTempDir();
  const pluginDir = scaffold(root, 'publiconly');
  const { publicKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
  const keyPath = path.join(root, 'public.pem');
  fs.writeFileSync(keyPath, publicKey.export({ type: 'spki', format: 'pem' }));
  const result = runPack(pluginDir, { key: keyPath });
  assert.strictEqual(result.success, false);
  assert.ok(result.errors.length >= 1);
});

test('signManifest matches a consumer-side verification of the same bytes', () => {
  const { privateKey, publicKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
  const raw = Buffer.from('{"id":"com.example.x"}');
  const signed = signManifest(raw, privateKey.export({ type: 'pkcs8', format: 'pem' }));
  assert.strictEqual(
    crypto.verify('sha256', raw, publicKey, Buffer.from(signed.signatureBase64, 'base64')),
    true
  );
  assert.strictEqual(signed.manifestSha256, crypto.createHash('sha256').update(raw).digest('hex'));
});

test('signing is deterministic: the same key and bytes produce the same publisher id', () => {
  const { privateKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
  const pem = privateKey.export({ type: 'pkcs8', format: 'pem' });
  assert.strictEqual(derivePublisherId(pem), derivePublisherId(pem));
});
