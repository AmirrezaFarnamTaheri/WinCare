const fs = require('fs');
const path = require('path');

const ID_REGEX = /^[a-z0-9]+(\.[a-z0-9]+)+$/;
const SEMVER_REGEX = /^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$/;
const ALLOWED_CATEGORIES = ['System Care', 'Security', 'Utilities', 'Privacy', 'Network'];
const ALLOWED_RISK_LEVELS = ['ReadOnly', 'Mutating', 'Elevated'];
const ALLOWED_EXECUTION_TYPES = ['Script', 'NativeBinary', 'Assembly'];

/**
 * Validates a wincare-plugin manifest and associated file assets.
 * @param {string} pluginDir - Absolute or relative path to plugin folder.
 * @returns {{ valid: boolean, errors: string[], warnings: string[], manifest: object|null }}
 */
function validatePlugin(pluginDir) {
  const errors = [];
  const warnings = [];
  const manifestPath = path.join(pluginDir, 'wincare-plugin.json');

  if (!fs.existsSync(manifestPath)) {
    return {
      valid: false,
      errors: [`Manifest file not found at: ${manifestPath}`],
      warnings: [],
      manifest: null
    };
  }

  let manifest;
  try {
    const rawContent = fs.readFileSync(manifestPath, 'utf8');
    manifest = JSON.parse(rawContent);
  } catch (err) {
    return {
      valid: false,
      errors: [`Invalid JSON in wincare-plugin.json: ${err.message}`],
      warnings: [],
      manifest: null
    };
  }

  // Validate ID
  if (!manifest.id || typeof manifest.id !== 'string') {
    errors.push('Manifest missing required string property: "id"');
  } else if (!ID_REGEX.test(manifest.id)) {
    errors.push(`Plugin ID "${manifest.id}" must be reverse-domain format with lowercase alphanumeric segments (e.g., "org.wincare.sample")`);
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

  // Validate Tools
  if (!Array.isArray(manifest.tools) || manifest.tools.length === 0) {
    errors.push('Manifest must contain a non-empty "tools" array');
  } else {
    manifest.tools.forEach((tool, index) => {
      const prefix = `Tool[${index}]`;
      if (!tool.id || typeof tool.id !== 'string') {
        errors.push(`${prefix} missing required string property "id"`);
      }
      if (!tool.name || typeof tool.name !== 'string') {
        errors.push(`${prefix} missing required string property "name"`);
      }
      if (tool.riskLevel && !ALLOWED_RISK_LEVELS.includes(tool.riskLevel)) {
        errors.push(`${prefix} invalid riskLevel "${tool.riskLevel}". Must be one of: ${ALLOWED_RISK_LEVELS.join(', ')}`);
      }
      if (tool.executionType && !ALLOWED_EXECUTION_TYPES.includes(tool.executionType)) {
        errors.push(`${prefix} invalid executionType "${tool.executionType}". Must be one of: ${ALLOWED_EXECUTION_TYPES.join(', ')}`);
      }

      // Check script/binary path security
      if (tool.scriptPath) {
        if (typeof tool.scriptPath !== 'string') {
          errors.push(`${prefix} "scriptPath" must be a relative string path`);
        } else {
          // Reject absolute paths
          if (path.isAbsolute(tool.scriptPath) || tool.scriptPath.startsWith('/') || tool.scriptPath.startsWith('\\')) {
            errors.push(`${prefix} "scriptPath" must not be an absolute path: "${tool.scriptPath}"`);
          }
          // Reject path traversal
          if (tool.scriptPath.includes('..')) {
            errors.push(`${prefix} "scriptPath" contains illegal path traversal (".."): "${tool.scriptPath}"`);
          }

          // Verify file exists
          const resolvedPath = path.resolve(pluginDir, tool.scriptPath);
          const canonicalDir = path.resolve(pluginDir);
          if (!resolvedPath.startsWith(canonicalDir)) {
            errors.push(`${prefix} scriptPath escapes plugin root directory`);
          } else if (!fs.existsSync(resolvedPath)) {
            errors.push(`${prefix} referenced script file does not exist: "${tool.scriptPath}"`);
          }
        }
      }
    });
  }

  return {
    valid: errors.length === 0,
    errors,
    warnings,
    manifest
  };
}

module.exports = {
  validatePlugin,
  ID_REGEX,
  SEMVER_REGEX,
  ALLOWED_CATEGORIES
};
