const fs = require('fs');
const path = require('path');
const { ID_REGEX } = require('../linter/manifestLinter');

// Identifier-style names only. Anything outside this set carries batch or C# metacharacters,
// and the name is interpolated into a generated namespace, a .cmd echo line, and C# string
// literals -- an "&" or a quote would become executable batch syntax or compilable C#.
const PLUGIN_NAME_REGEX = /^[A-Za-z0-9][A-Za-z0-9 ._]{0,63}$/;
const FALLBACK_ID = 'com.community.plugin';

/**
 * Escapes a value for use inside a generated C# double-quoted string literal.
 */
function escapeCsString(value) {
  return String(value === undefined || value === null ? '' : value)
    .replace(/[\r\n\t]+/g, ' ')
    .replace(/\\/g, '\\\\')
    .replace(/"/g, '\\"');
}

function createPlugin(targetName, options = {}) {
  const pluginName = targetName || 'my-custom-tool';
  if (!PLUGIN_NAME_REGEX.test(pluginName)) {
    throw new Error(`Invalid plugin name "${pluginName}". Use 1 to 64 identifier characters (letters, digits, spaces, dots, or underscores); shell and C# metacharacters are rejected.`);
  }
  const templateType = options.template || 'json-pack';
  if (!['json-pack', 'csharp-plugin'].includes(templateType)) {
    throw new Error(`Unsupported template '${templateType}'. Use json-pack or csharp-plugin.`);
  }

  // Keep the scaffold inside the current directory unless --outDir explicitly points
  // elsewhere, so a crafted name cannot direct the write at an unrelated location.
  const targetDir = path.resolve(options.outDir || path.join(process.cwd(), pluginName));
  if (!options.outDir) {
    const relative = path.relative(path.resolve(process.cwd()), targetDir);
    if (relative === '' || relative === '..' || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
      throw new Error(`Refusing to create a plugin directory outside the current directory: ${targetDir}`);
    }
  }

  if (fs.existsSync(targetDir)) {
    throw new Error(`Target directory already exists: ${targetDir}`);
  }

  fs.mkdirSync(targetDir, { recursive: true });

  // The generated id must survive its own validation: fall back rather than emit an id
  // neither the linter nor the installer accepts while reporting success.
  const generatedId = 'com.community.' + pluginName.toLowerCase().replace(/[^a-z0-9]/g, '');
  const safeId = ID_REGEX.test(generatedId) ? generatedId : FALLBACK_ID;
  const displayName = pluginName.charAt(0).toUpperCase() + pluginName.slice(1);
  const author = escapeCsString(options.author || 'Community Developer');
  const description = escapeCsString(options.description || 'High performance managed assembly plugin');
  const namespaceName = 'Community.' + pluginName.replace(/[^a-zA-Z0-9]/g, '');

  if (templateType === 'csharp-plugin') {
    const manifest = {
      id: safeId,
      name: displayName,
      version: "1.0.0",
      author: options.author || "Community Developer",
      description: options.description || "High performance managed assembly plugin",
      category: options.category || "Utilities",
      entryType: "Assembly",
      targetFramework: "net8.0-windows10.0.19041.0",
      assemblyFileName: "PluginAssembly.dll",
      pluginClassName: `${namespaceName}.PluginEntryPoint`,
      tools: [
        {
          id: `${safeId}.execute`,
          title: `${pluginName} Execution Engine`,
          summary: "High performance managed assembly plugin",
          area: "Utilities",
          section: "General",
          risk: "ReadOnly",
          readOnly: true,
          administratorAccess: "No",
          restart: "No"
        }
      ]
    };

    fs.writeFileSync(path.join(targetDir, 'wincare-plugin.json'), JSON.stringify(manifest, null, 2));

    const projectContent = `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>
    <GenerateSupportedOSPlatformAttribute>true</GenerateSupportedOSPlatformAttribute>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>PluginAssembly</AssemblyName>
  </PropertyGroup>
  <!-- Set WinCareSdkRoot to a directory containing WinCare contract assemblies. -->
  <ItemGroup>
    <Reference Include="WinCare.Application" HintPath="$(WinCareSdkRoot)\\WinCare.Application.dll" Private="false" />
    <Reference Include="WinCare.CommandCatalog" HintPath="$(WinCareSdkRoot)\\WinCare.CommandCatalog.dll" Private="false" />
  </ItemGroup>
</Project>
`;
    fs.writeFileSync(path.join(targetDir, 'PluginAssembly.csproj'), projectContent);

    const csContent = `using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Plugins;
using WinCare.CommandCatalog.Models;

namespace ${namespaceName}
{
    public class PluginEntryPoint : IWinCarePlugin
    {
        public string Id => "${safeId}";
        public string Name => "${displayName}";
        public string Version => "1.0.0";
        public string Author => "${author}";
        public string Description => "${description}";

        public Task InitializeAsync(IPluginHost host, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public IReadOnlyList<CommandDefinition> GetCommands()
        {
            return Array.Empty<CommandDefinition>();
        }

        public IReadOnlyList<IPluginWidget> GetWidgets()
        {
            return Array.Empty<IPluginWidget>();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
`;
    fs.writeFileSync(path.join(targetDir, 'PluginEntryPoint.cs'), csContent);
  } else {
    // Default json-pack
    const scriptsDir = path.join(targetDir, 'scripts');
    fs.mkdirSync(scriptsDir, { recursive: true });

    const scriptRelativePath = 'scripts/clean_temp.cmd';
    fs.writeFileSync(path.join(targetDir, scriptRelativePath), `@echo off\nREM WinCare Custom Tool Script\necho Executing ${pluginName} safe maintenance...\nexit /b 0\n`);

    const manifest = {
      id: safeId,
      name: displayName,
      version: "1.0.0",
      author: options.author || "Community Developer",
      description: options.description || "Custom community script maintenance tool",
      category: options.category || "System Care",
      entryType: "Manifest",
      tools: [
        {
          id: `${safeId}.clean`,
          title: `Run ${pluginName}`,
          summary: "Custom community script maintenance tool",
          area: "System care",
          section: "Storage",
          risk: "ReadOnly",
          readOnly: true,
          administratorAccess: "No",
          restart: "No",
          scriptPath: scriptRelativePath
        }
      ]
    };

    fs.writeFileSync(path.join(targetDir, 'wincare-plugin.json'), JSON.stringify(manifest, null, 2));
  }

  return {
    targetDir,
    id: safeId,
    template: templateType
  };
}

module.exports = {
  createPlugin,
  PLUGIN_NAME_REGEX,
  escapeCsString
};
