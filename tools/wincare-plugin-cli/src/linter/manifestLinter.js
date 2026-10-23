const fs = require('fs');
const path = require('path');

// Reverse-domain plugin identifier. The 3-128 character span matches the installer's
// PluginIdRegex (^[a-zA-Z0-9][a-zA-Z0-9._-]{2,127}$ in PluginInstallerService.cs) exactly;
// the character set is deliberately narrower, because the SDK requires lowercase
// reverse-domain ids so that an id is also a stable, case-normal catalog key.
const ID_REGEX = /^(?=.{3,128}$)[a-z0-9]+(\.[a-z0-9]+)+$/;
const SEMVER_REGEX = /^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$/;
const ALLOWED_CATEGORIES = ['System Care', 'Security', 'Utilities', 'Privacy', 'Network'];
const CANONICAL_MANIFEST = 'wincare-plugin.json';
const FALLBACK_MANIFEST = 'plugin.json';
// Mirrors PluginInstallerService.MaxManifestBytes; both consumer layers enforce the same cap.
const MAX_MANIFEST_BYTES = 1024 * 1024;
// The five tiers the runtime's CommandRisk enum actually parses (CommandDefinition.cs).
const ALLOWED_RISKS = ['ReadOnly', 'Low', 'Moderate', 'High', 'Critical'];
// Legacy "riskLevel" spellings and the tier the loader maps them to (PluginManifest.cs).
const LEGACY_RISK_LEVEL_MAP = { Mutating: 'Moderate', Elevated: 'High' };
const LEGACY_RISK_LEVELS = [...ALLOWED_RISKS, ...Object.keys(LEGACY_RISK_LEVEL_MAP)];
// Extensions the runtime's PluginScriptCommandHandler dispatches on. Anything else is
// launched directly as an executable (PluginScriptCommandHandler.cs:95-154, 214-220).
const SCRIPT_EXTENSIONS = ['.ps1', '.cmd', '.bat'];
const SIGNATURE_FILE = 'wincare-plugin.sig';

/**
 * Strips an optional UTF-8 BOM for JSON decoding only. The raw bytes passed in are unchanged
 * and remain the span bound to digests and signatures, exactly like the consumer's
 * PluginAdmissionTrustStore.DecodeManifestJson.
 */
function decodeManifestJson(rawBytes) {
  const bytes = rawBytes.length >= 3 && rawBytes[0] === 0xEF && rawBytes[1] === 0xBB && rawBytes[2] === 0xBF
    ? rawBytes.subarray(3)
    : rawBytes;
  return bytes.toString('utf8');
}

/**
 * Validates a wincare-plugin manifest and associated file assets.
 * @param {string} pluginDir - Absolute or relative path to plugin folder.
 * @returns {{ valid: boolean, errors: string[], warnings: string[], manifest: object|null, manifestPath: string|null }}
 */
function validatePlugin(pluginDir) {
  const errors = [];
  const warnings = [];

  const canonicalPath = path.join(pluginDir, CANONICAL_MANIFEST);
  const fallbackPath = path.join(pluginDir, FALLBACK_MANIFEST);
  let manifestPath = canonicalPath;
  if (!fs.existsSync(manifestPath)) {
    if (!fs.existsSync(fallbackPath)) {
      return {
        valid: false,
        errors: [`Manifest file not found at: ${canonicalPath} (the legacy fallback "${FALLBACK_MANIFEST}" is also accepted)`],
        warnings: [],
        manifest: null,
        manifestPath: null
      };
    }
    manifestPath = fallbackPath;
    warnings.push(`Using legacy manifest filename "${FALLBACK_MANIFEST}"; "${CANONICAL_MANIFEST}" is canonical. Rename it so the CLI and the runtime agree.`);
  }

  let manifest;
  try {
    // Read once and keep the raw span: digests and signatures are bound to these bytes.
    const rawContent = fs.readFileSync(manifestPath);
    if (rawContent.length > MAX_MANIFEST_BYTES) {
      errors.push(`Manifest exceeds the ${MAX_MANIFEST_BYTES}-byte size limit (${rawContent.length} bytes); the installer and loader both reject it.`);
    }
    manifest = JSON.parse(decodeManifestJson(rawContent));
  } catch (err) {
    return {
      valid: false,
      errors: [`Invalid JSON in ${path.basename(manifestPath)}: ${err.message}`],
      warnings,
      manifest: null,
      manifestPath
    };
  }

  // Validate ID
  if (!manifest.id || typeof manifest.id !== 'string') {
    errors.push('Manifest missing required string property: "id"');
  } else if (!ID_REGEX.test(manifest.id)) {
    errors.push(`Plugin ID "${manifest.id}" must be reverse-domain format with lowercase alphanumeric segments (e.g., "org.wincare.sample") and 3 to 128 characters`);
  }

  // Validate Name
  if (!manifest.name || typeof manifest.name !== 'string') {
    errors.push('Manifest missing required string property: "name"');
  } else if (manifest.name.length > 50) {
    errors.push('Plugin name must be 50 characters or fewer');
  }

  // Validate Version
  if (!manifest.version || typeof manifest.version !== 'string') {
    errors.push('Manifest missing required string property: "version"');
  } else if (!SEMVER_REGEX.test(manifest.version)) {
    errors.push(`Version "${manifest.version}" does not conform to SemVer (e.g., "1.0.0" or "1.0.0-rc1")`);
  }

  // Validate Author
  if (!manifest.author || typeof manifest.author !== 'string') {
    errors.push('Manifest missing required string property: "author"');
  }

  // Validate Category
  if (!manifest.category || typeof manifest.category !== 'string') {
    errors.push('Manifest missing required string property: "category"');
  } else if (!ALLOWED_CATEGORIES.includes(manifest.category)) {
    warnings.push(`Category "${manifest.category}" is not in standard list (${ALLOWED_CATEGORIES.join(', ')}). It will appear under "Utilities".`);
  }

  // Validate Assembly target + declared assembly existence.
  // The installer records an assembly digest only when this file exists, and deliberately
  // leaves the assembly unbound when it does not; shipping a DLL-less package is exactly the
  // state that leaves a later swap unverified, so fail here instead of at install time.
  const assemblyFileName = manifest.assemblyFileName || manifest.assemblyEntry;
  const isAssembly = manifest.entryType === 'Assembly' || Boolean(assemblyFileName);
  if (isAssembly && manifest.targetFramework !== 'net8.0-windows10.0.19041.0') {
    errors.push('Assembly plugins must declare targetFramework "net8.0-windows10.0.19041.0"');
  }
  if (!assemblyFileName) {
    if (isAssembly) {
      errors.push('Assembly plugins must declare "assemblyFileName" pointing at the compiled plugin assembly');
    }
  } else if (typeof assemblyFileName !== 'string' || assemblyFileName.trim().length === 0) {
    errors.push('Manifest "assemblyFileName" must be a non-empty string');
  } else {
    const resolvedAssembly = path.resolve(pluginDir, assemblyFileName);
    const canonicalDir = path.resolve(pluginDir);
    if (path.isAbsolute(assemblyFileName) || assemblyFileName.includes('..') ||
        path.resolve(resolvedAssembly) !== resolvedAssembly ||
        !resolvedAssembly.startsWith(canonicalDir)) {
      errors.push(`Manifest "assemblyFileName" must be a relative path inside the plugin root directory: "${assemblyFileName}"`);
    } else if (!fs.existsSync(resolvedAssembly)) {
      errors.push(`Referenced assembly file does not exist: "${assemblyFileName}"`);
    }
  }

  // A signature shipped inside the package is not an independent trust assertion: the
  // installer reads wincare-plugin.sig (or an inline "signature" property) and rejects the
  // package unless the caller supplied the catalog's signature out-of-band instead.
  if (fs.existsSync(path.join(pluginDir, SIGNATURE_FILE))) {
    warnings.push(`A "${SIGNATURE_FILE}" file inside the plugin directory will be bundled and then rejected at install ("Package-supplied signature is not an independent trust assertion"). Publish the signature through the catalog instead; "pack --key <publisher-key.pem>" writes it next to the archive.`);
  }
  if (manifest.signature !== undefined && manifest.signature !== null && String(manifest.signature).trim() !== '') {
    warnings.push('An inline manifest "signature" property is rejected at install for the same reason as a bundled signature file; supply the signature through the catalog instead.');
  }

  // Validate Tools
  if (!Array.isArray(manifest.tools) || manifest.tools.length === 0) {
    errors.push('Manifest must contain a non-empty "tools" array');
  } else {
    manifest.tools.forEach((tool, index) => {
      const prefix = `Tool[${index}]`;
      if (!tool.id || typeof tool.id !== 'string') {
        errors.push(`${prefix} missing required string property "id"`);
      }

      const toolTitle = tool.title || tool.name;
      if (!toolTitle || typeof toolTitle !== 'string') {
        errors.push(`${prefix} missing required string property "title" (or "name")`);
      }

      // The canonical "risk" field is parsed raw and Enum.TryParse'd by the runtime, so only
      // the five tiers above are legal on it. "Mutating"/"Elevated" are legacy "riskLevel"
      // spellings that the loader maps to Moderate/High; on "risk" they throw at load time.
      if (tool.risk && !ALLOWED_RISKS.includes(tool.risk)) {
        errors.push(`${prefix} invalid risk "${tool.risk}". Must be one of: ${ALLOWED_RISKS.join(', ')}`);
      }
      if (tool.riskLevel && !LEGACY_RISK_LEVELS.includes(tool.riskLevel)) {
        errors.push(`${prefix} invalid riskLevel "${tool.riskLevel}". Must be one of: ${LEGACY_RISK_LEVELS.join(', ')}`);
      }
      if (tool.riskLevel && LEGACY_RISK_LEVEL_MAP[tool.riskLevel]) {
        warnings.push(`${prefix} legacy "riskLevel: ${tool.riskLevel}" is read as risk "${LEGACY_RISK_LEVEL_MAP[tool.riskLevel]}" at load; prefer "risk" instead.`);
      }

      // "executorType" has no consumer: the runtime dispatches scripts on the scriptPath file
      // extension, not on a declared executor kind. The extension check below is the gate that
      // actually matches runtime behaviour, so the field is accepted silently rather than
      // validated against a list nothing enforces.

      // Check script/binary path security
      const scriptPath = tool.scriptPath || tool.script;
      if (scriptPath) {
        if (typeof scriptPath !== 'string') {
          errors.push(`${prefix} "scriptPath" must be a relative string path`);
        } else {
          // Reject absolute paths
          if (path.isAbsolute(scriptPath) || scriptPath.startsWith('/') || scriptPath.startsWith('\\')) {
            errors.push(`${prefix} "scriptPath" must not be an absolute path: "${scriptPath}"`);
          }
          // Reject path traversal
          if (scriptPath.includes('..')) {
            errors.push(`${prefix} "scriptPath" contains illegal path traversal (".."): "${scriptPath}"`);
          }

          // Verify file exists
          const resolvedPath = path.resolve(pluginDir, scriptPath);
          const canonicalDir = path.resolve(pluginDir);
          if (!resolvedPath.startsWith(canonicalDir)) {
            errors.push(`${prefix} scriptPath escapes plugin root directory`);
          } else if (!fs.existsSync(resolvedPath)) {
            errors.push(`${prefix} referenced script file does not exist: "${scriptPath}"`);
          } else {
            // Warn rather than fail: the runtime's fallthrough branch launches any other
            // extension directly as an executable, so an error here would be a gate the
            // runtime does not enforce.
            const extension = path.extname(scriptPath).toLowerCase();
            if (extension && !SCRIPT_EXTENSIONS.includes(extension)) {
              warnings.push(`${prefix} scriptPath "${scriptPath}" has extension "${extension}", which the runtime launches directly as an executable rather than through a script host. Only ${SCRIPT_EXTENSIONS.join(' / ')} are interpreted.`);
            }
          }
        }
      }
    });
  }

  return {
    valid: errors.length === 0,
    errors,
    warnings,
    manifest,
    manifestPath
  };
}

module.exports = {
  validatePlugin,
  decodeManifestJson,
  ID_REGEX,
  SEMVER_REGEX,
  ALLOWED_CATEGORIES,
  MAX_MANIFEST_BYTES,
  CANONICAL_MANIFEST,
  FALLBACK_MANIFEST
};
