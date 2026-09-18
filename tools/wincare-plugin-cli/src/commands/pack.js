const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { validatePlugin } = require('../linter/manifestLinter');
const { packPlugin, DIGEST_SUFFIX, TRUST_SUFFIX } = require('../packager/zipBuilder');

/**
 * SHA-256 as lowercase hex, matching the consumer's Convert.ToHexString(...).ToLowerInvariant().
 */
function sha256Hex(data) {
  return crypto.createHash('sha256').update(data).digest('hex');
}

/**
 * Derives a stable publisher identity from the public key (the manifest record calls it a
 * "publisher unique identifier or certificate thumbprint"): the first 16 hex digits of the
 * SHA-256 of the key's DER-encoded SubjectPublicKeyInfo.
 */
function derivePublisherId(privateKeyPem) {
  const spki = crypto.createPublicKey(privateKeyPem).export({ type: 'spki', format: 'der' });
  return 'pub-' + crypto.createHash('sha256').update(spki).digest('hex').slice(0, 16);
}

/**
 * Signs a manifest. Byte semantics, matched 1:1 against
 * PluginAdmissionTrustStore.VerifyManifestSignature (src/WinCare.Application/Plugins/PluginSecurityPolicy.cs):
 *
 *   - the signed buffer is the manifest file's bytes read verbatim from disk. The JSON is
 *     never parsed and re-serialized, so byte-level formatting is part of the signed span,
 *     and an optional UTF-8 BOM stays inside it;
 *   - an RSA key signs PKCS#1 v1.5 over SHA-256 and an EC key signs ECDSA over SHA-256
 *     (crypto.sign with no algorithm defaults to exactly those);
 *   - the signature is emitted base64, the encoding VerifyManifestSignature decodes;
 *   - the manifest digest is lowercase hex SHA-256 of those same raw bytes.
 *
 * @returns {{ signatureBase64: string, manifestSha256: string }}
 */
function signManifest(rawManifestBytes, privateKeyPem) {
  const key = crypto.createPrivateKey(privateKeyPem);
  const signature = crypto.sign(null, rawManifestBytes, key);
  return {
    signatureBase64: signature.toString('base64'),
    manifestSha256: sha256Hex(rawManifestBytes)
  };
}

/**
 * Emits the trust metadata the plugin catalog consumes: an archive digest next to the
 * archive, and a signed-manifest record carrying the publisher key, signature, and both
 * digests. Neither file is part of the installable package -- the installer rejects a
 * signature that ships inside the archive -- so both are written beside the archive.
 */
function signPackage(packResult, manifest, manifestPath, keyPath) {
  let privateKeyPem;
  try {
    privateKeyPem = fs.readFileSync(keyPath, 'utf8');
  } catch (err) {
    return { success: false, errors: [`Could not read the publisher key at "${keyPath}": ${err.message}`] };
  }

  let publicKeyPem;
  try {
    publicKeyPem = crypto.createPublicKey(privateKeyPem).export({ type: 'spki', format: 'pem' });
  } catch (err) {
    return { success: false, errors: [`The key at "${keyPath}" is not a usable PEM private key: ${err.message}`] };
  }

  const rawManifestBytes = fs.readFileSync(manifestPath);
  const archiveBytes = fs.readFileSync(packResult.outputPath);
  const archiveSha256 = sha256Hex(archiveBytes);
  const { signatureBase64, manifestSha256 } = signManifest(rawManifestBytes, privateKeyPem);
  const publisherId = derivePublisherId(privateKeyPem);

  const digestPath = packResult.outputPath + DIGEST_SUFFIX;
  const trustPath = packResult.outputPath + TRUST_SUFFIX;
  fs.writeFileSync(digestPath, `${archiveSha256} *${path.basename(packResult.outputPath)}\n`);
  fs.writeFileSync(trustPath, JSON.stringify({
    schemaVersion: 1,
    pluginId: manifest.id,
    publisherId,
    publisherPublicKeyPem: publicKeyPem,
    publisherSignature: signatureBase64,
    archiveSha256,
    manifestSha256
  }, null, 2) + '\n');

  console.log(`✓ Signed manifest (sha256 ${manifestSha256}) as publisher ${publisherId}`);
  console.log(`✓ Archive digest (sha256 ${archiveSha256}) written to ${digestPath}`);
  console.log(`✓ Trust metadata written to ${trustPath} -- hand this to the catalog; it is not part of the installable package.`);

  return Object.assign({}, packResult, {
    signed: true,
    publisherId,
    archiveSha256,
    manifestSha256,
    digestPath,
    trustPath
  });
}

/**
 * Validates and packages a plugin directory.
 * @param {string} targetDir - Directory containing the plugin manifest.
 * @param {{ output: string|null, key: string|null }|string} [options]
 *   `output` overrides the archive path; `key` is a PEM private key whose publisher signs the
 *   manifest. A string is accepted as the output path for backwards compatibility.
 * @returns {{ success: boolean, errors?: string[], outputPath?: string, fileCount?: number, sizeBytes?: number }}
 */
function runPack(targetDir, options) {
  const opts = typeof options === 'string' ? { output: options } : (options || {});
  const pluginDir = targetDir || process.cwd();
  const validation = validatePlugin(pluginDir);

  if (validation.warnings.length > 0) {
    console.warn('Warnings:');
    validation.warnings.forEach(w => console.warn(`  - ⚠ ${w}`));
  }

  if (!validation.valid) {
    console.error('Packaging aborted due to validation errors:');
    validation.errors.forEach(err => console.error(`  - ✗ ${err}`));
    return { success: false, errors: validation.errors };
  }

  const manifest = validation.manifest;
  const defaultArchiveName = `${manifest.id}-${manifest.version}.wincare-plugin`;
  const outPath = opts.output || path.join(pluginDir, defaultArchiveName);

  let packResult;
  try {
    packResult = packPlugin(pluginDir, outPath);
  } catch (err) {
    // A packaging failure must surface through the documented { success: false } contract
    // rather than throwing, and must never leave a truncated archive behind.
    console.error(`Packaging failed: ${err.message}`);
    return { success: false, errors: [err.message] };
  }

  console.log(`✓ Packaged "${manifest.name}" into archive: ${packResult.outputPath} (${(packResult.sizeBytes / 1024).toFixed(2)} KB, ${packResult.fileCount} files)`);

  if (!opts.key) {
    console.warn('⚠ This archive is unsigned and has no recorded digest. It cannot be installed through the plugin store until the manifest is signed and the digest is recorded by an independently trusted catalog. Re-run with --key <publisher-key.pem> to emit that metadata.');
    return packResult;
  }

  try {
    return signPackage(packResult, manifest, validation.manifestPath, opts.key);
  } catch (err) {
    // A key problem must surface through the same contract as a packaging failure.
    console.error(`Signing failed: ${err.message}`);
    return { success: false, errors: [err.message] };
  }
}

module.exports = {
  runPack,
  signManifest,
  derivePublisherId,
  sha256Hex
};
