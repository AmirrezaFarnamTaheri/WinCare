const fs = require('fs');
const path = require('path');

function createPlugin(targetName, options = {}) {
  const pluginName = targetName || 'my-custom-tool';
  const templateType = options.template || 'json-pack';
  if (!['json-pack', 'csharp-plugin'].includes(templateType)) {
    throw new Error(`Unsupported template '${templateType}'. Use json-pack or csharp-plugin.`);
  }
  const targetDir = path.resolve(options.outDir || pluginName);

  if (fs.existsSync(targetDir)) {
    throw new Error(`Target directory already exists: ${targetDir}`);
  }

  fs.mkdirSync(targetDir, { recursive: true });

  const safeId = 'com.community.' + pluginName.toLowerCase().replace(/[^a-z0-9]/g, '');

  if (templateType === 'csharp-plugin') {
    const manifest = {
      id: safeId,
      name: pluginName.charAt(0).toUpperCase() + pluginName.slice(1),
      version: "1.0.0",
      author: options.author || "Community Developer",
      description: options.description || "High performance managed assembly plugin",
      category: options.category || "Utilities",
      entryType: "Assembly",
      assemblyFileName: "PluginAssembly.dll",
      pluginClassName: `Community.${pluginName.replace(/[^a-zA-Z0-9]/g, '')}.PluginEntryPoint`,
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
          restart: "No",
          executorType: "Assembly"
        }
      ]
    };

    fs.writeFileSync(path.join(targetDir, 'wincare-plugin.json'), JSON.stringify(manifest, null, 2));

    const csContent = `using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Plugins;
using WinCare.CommandCatalog.Models;

namespace Community.${pluginName.replace(/[^a-zA-Z0-9]/g, '')}
{
    public class PluginEntryPoint : IWinCarePlugin
    {
        public string Id => "${safeId}";
        public string Name => "${pluginName.charAt(0).toUpperCase() + pluginName.slice(1)}";
        public string Version => "1.0.0";
        public string Author => "${options.author || 'Community Developer'}";
        public string Description => "${options.description || 'High performance managed assembly plugin'}";

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
      name: pluginName.charAt(0).toUpperCase() + pluginName.slice(1),
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
          executorType: "Script",
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
  createPlugin
};
