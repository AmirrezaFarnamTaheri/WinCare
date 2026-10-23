'use strict';

/**
 * Minimal, dependency-free argument parser shared by every CLI command. Before this existed
 * each command parsed argv by hand, and `pack` took its output path positionally -- so
 * `pack myplugin --out x.zip` used "--out" as the file name. One parser keeps every command
 * consistent and lets a flag be rejected when its value is another flag.
 *
 * @param {string[]} args - Raw argv tokens, typically process.argv.slice(2).
 * @param {Object.<string, {boolean?: boolean, alias?: string}>} spec - The accepted flags.
 *   A flag declared with `boolean: true` takes no value; every other flag consumes the
 *   following token, or an inline `--flag=value`.
 * @returns {{ positionals: string[], flags: Object.<string, string|boolean> }}
 * @throws {Error} on an unknown flag, a flag missing its value, or a value starting with "-".
 */
function parseFlags(args, spec) {
  const tokens = Array.isArray(args) ? args : [];
  const positionals = [];
  const flags = {};

  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i];

    if (token === '--') {
      positionals.push(...tokens.slice(i + 1));
      break;
    }

    if (!token.startsWith('-') || token === '-') {
      positionals.push(token);
      continue;
    }

    const equalsIndex = token.indexOf('=');
    const rawName = equalsIndex === -1 ? token : token.slice(0, equalsIndex);
    const name = rawName.startsWith('--') ? rawName.slice(2) : rawName.slice(1);
    const definition = spec && spec[name];

    if (!definition) {
      throw new Error(`Unknown option "${rawName}".`);
    }

    const inlineValue = equalsIndex === -1 ? null : token.slice(equalsIndex + 1);
    let value;
    if (definition.boolean) {
      value = inlineValue === null ? true : String(inlineValue).toLowerCase() !== 'false';
    } else if (inlineValue !== null) {
      value = inlineValue;
    } else {
      value = tokens[++i];
      if (value === undefined) {
        throw new Error(`Option "${rawName}" requires a value.`);
      }
    }

    if (!definition.boolean && String(value).startsWith('-')) {
      throw new Error(`Option "${rawName}" must not be given a value that begins with "-".`);
    }

    flags[definition.alias || name] = value;
  }

  return { positionals, flags };
}

module.exports = { parseFlags };
