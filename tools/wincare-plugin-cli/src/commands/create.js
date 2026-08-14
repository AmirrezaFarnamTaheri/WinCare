const fs = require('fs');
const path = require('path');

function createPlugin(targetName, options = {}) {
  const pluginName = targetName || 'my-custom-tool';
  const templateType = options.template || 'json-pack';
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
      category: options.category || "Utilities",
      assemblyEntry: "PluginAssembly.dll",
      tools: [
        {
          id: `${safeId}.execute`,
          name: `${pluginName} Execution Engine`,
          description: "High performance managed assembly plugin",
          riskLevel: "ReadOnly",
          executionType: "Assembly"
        }
      ]
    };

    fs.writeFileSync(path.join(targetDir, 'wincare-plugin.json'), JSON.stringify(manifest, null, 2));

    const csContent = `using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Plugins;

namespace Community.${pluginName.replace(/[^a-zA-Z0-9]/g, '')}
{
    public class PluginEntryPoint : IWinCarePlugin
    {
        public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public IEnumerable<object> GetCommands()
        {
            yield break;
        }

        public IEnumerable<object> GetWidgets()
        {
            yield break;
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
      category: options.category || "System Care",
      tools: [
        {
          id: `${safeId}.clean`,
          name: `Run ${pluginName}`,
          description: "Custom community script maintenance tool",
          riskLevel: "ReadOnly",
          executionType: "Script",
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
