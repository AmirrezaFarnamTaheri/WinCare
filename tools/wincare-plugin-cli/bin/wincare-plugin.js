#!/usr/bin/env node

const { parseFlags } = require('../src/util/parseFlags');
const { createPlugin } = require('../src/commands/create');
const { runValidate } = require('../src/commands/validate');
const { runPack } = require('../src/commands/pack');

const args = process.argv.slice(2);
const command = args[0];

function printHelp() {
  console.log(`
WinCare Community Plugin SDK & Developer CLI

Usage:
  wincare-plugin <command> [options]

Commands:
  create <name>       Scaffold a new plugin directory
                      Options: --template <json-pack|csharp-plugin> --outDir <path>
  validate [dir]      Lint and verify plugin manifest and security boundaries
  pack [dir]          Validate and package plugin into .wincare-plugin ZIP archive
                      Options: --out <archive> --key <publisher-key.pem>
  help                Show this help message

Signing:
  "pack --key <publisher-key.pem>" emits the trust metadata the plugin catalog consumes,
  next to the archive: an archive digest (<archive>.sha256) and a signed manifest record
  (<archive>.sig.json). The signature covers the exact raw manifest bytes, so it is not
  affected by how the JSON is formatted. An unsigned archive cannot be installed through
  the store; neither can a signature shipped inside the package.
`);
}

if (!command || command === 'help' || command === '--help' || command === '-h') {
  printHelp();
  process.exit(0);
}

try {
  if (command === 'create') {
    const { positionals, flags } = parseFlags(args.slice(1), {
      template: {},
      outDir: {}
    });
    const name = positionals[0];
    const template = flags.template || 'json-pack';

    if (!['json-pack', 'csharp-plugin'].includes(template)) {
      throw new Error(`Unsupported template '${template}'. Use json-pack or csharp-plugin.`);
    }

    const result = createPlugin(name, { template, outDir: flags.outDir || null });
    console.log(`✓ Created plugin "${result.id}" using template "${result.template}" in ${result.targetDir}`);
  } else if (command === 'validate') {
    const { positionals } = parseFlags(args.slice(1), {});
    const result = runValidate(positionals[0] || '.');
    if (!result.valid) {
      process.exit(1);
    }
  } else if (command === 'pack') {
    const { positionals, flags } = parseFlags(args.slice(1), {
      out: { alias: 'output' },
      output: {},
      key: {}
    });
    const result = runPack(positionals[0] || '.', {
      output: flags.output || null,
      key: flags.key || null
    });
    if (!result.success) {
      process.exit(1);
    }
  } else {
    console.error(`Unknown command: ${command}`);
    printHelp();
    process.exit(1);
  }
} catch (err) {
  console.error(`Error: ${err.message}`);
  process.exit(1);
}
