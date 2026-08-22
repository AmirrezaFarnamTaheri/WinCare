#!/usr/bin/env node

const path = require('path');
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
  help                Show this help message
`);
}

if (!command || command === 'help' || command === '--help' || command === '-h') {
  printHelp();
  process.exit(0);
}

try {
  if (command === 'create') {
    const name = args[1];
    let template = 'json-pack';
    let outDir = null;

    for (let i = 2; i < args.length; i++) {
      if (args[i] === '--template' && args[i + 1]) {
        template = args[++i];
      } else if (args[i] === '--outDir' && args[i + 1]) {
        outDir = args[++i];
      }
    }

    const result = createPlugin(name, { template, outDir });
    console.log(`✓ Created plugin "${result.id}" using template "${result.template}" in ${result.targetDir}`);
  } else if (command === 'validate') {
    const target = args[1] || '.';
    const result = runValidate(target);
    if (!result.valid) {
      process.exit(1);
    }
  } else if (command === 'pack') {
    const target = args[1] || '.';
    const customOut = args[2] || null;
    const result = runPack(target, customOut);
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
