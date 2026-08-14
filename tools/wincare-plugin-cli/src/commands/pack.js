const path = require('path');
const { validatePlugin } = require('../linter/manifestLinter');
const { packPlugin } = require('../packager/zipBuilder');

function runPack(targetDir, customOutputPath) {
  const pluginDir = targetDir || process.cwd();
  const validation = validatePlugin(pluginDir);

  if (!validation.valid) {
    console.error('Packaging aborted due to validation errors:');
    validation.errors.forEach(err => console.error(`  - ✗ ${err}`));
    return { success: false, errors: validation.errors };
  }

  const manifest = validation.manifest;
  const defaultArchiveName = `${manifest.id}-${manifest.version}.wincare-plugin`;
  const outPath = customOutputPath || path.join(pluginDir, defaultArchiveName);

  const packResult = packPlugin(pluginDir, outPath);
  console.log(`✓ Packaged "${manifest.name}" into archive: ${packResult.outputPath} (${(packResult.sizeBytes / 1024).toFixed(2)} KB, ${packResult.fileCount} files)`);

  return packResult;
}

module.exports = {
  runPack
};
