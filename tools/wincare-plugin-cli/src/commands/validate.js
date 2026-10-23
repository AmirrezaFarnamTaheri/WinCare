const { validatePlugin } = require('../linter/manifestLinter');

function runValidate(targetDir) {
  const result = validatePlugin(targetDir || process.cwd());

  if (result.errors.length > 0) {
    console.error('Validation FAILED with errors:');
    result.errors.forEach(err => console.error(`  - ✗ ${err}`));
  }

  if (result.warnings.length > 0) {
    console.warn('Warnings:');
    result.warnings.forEach(w => console.warn(`  - ⚠ ${w}`));
  }

  if (result.valid) {
    console.log(`✓ Plugin manifest and security checks passed cleanly! (ID: ${result.manifest.id}, Version: ${result.manifest.version})`);
  }

  return result;
}

module.exports = {
  runValidate
};
