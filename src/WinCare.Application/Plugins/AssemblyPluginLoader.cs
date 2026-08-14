namespace WinCare.Application.Plugins;

using System.Reflection;

/// <summary>
/// Result of loading an assembly plugin.
/// </summary>
public sealed record AssemblyPluginLoadResult(
    bool Success,
    IWinCarePlugin? Plugin,
    PluginLoadContext? LoadContext,
    string? ErrorMessage
);

/// <summary>
/// Dynamically loads compiled C# plugin assemblies (.dll) in isolated AssemblyLoadContexts.
/// </summary>
public static class AssemblyPluginLoader
{
    public static AssemblyPluginLoadResult LoadPluginAssembly(string assemblyPath, string? expectedPluginClassName = null)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
        {
            return new AssemblyPluginLoadResult(false, null, null, $"Assembly file does not exist: {assemblyPath}");
        }

        PluginLoadContext? loadContext = null;
        try
        {
            loadContext = new PluginLoadContext(assemblyPath);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

            Type? pluginType = null;

            if (!string.IsNullOrWhiteSpace(expectedPluginClassName))
            {
                pluginType = assembly.GetType(expectedPluginClassName);
            }

            if (pluginType == null)
            {
                pluginType = assembly.DefinedTypes
                    .FirstOrDefault(t => typeof(IWinCarePlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
            }

            if (pluginType == null)
            {
                loadContext.Unload();
                return new AssemblyPluginLoadResult(false, null, null, $"No implementation of '{nameof(IWinCarePlugin)}' found in assembly.");
            }

            var instance = Activator.CreateInstance(pluginType) as IWinCarePlugin;
            if (instance == null)
            {
                loadContext.Unload();
                return new AssemblyPluginLoadResult(false, null, null, $"Failed to instantiate plugin type '{pluginType.FullName}'.");
            }

            return new AssemblyPluginLoadResult(true, instance, loadContext, null);
        }
        catch (Exception ex)
        {
            loadContext?.Unload();
            return new AssemblyPluginLoadResult(false, null, null, $"Assembly load failed: {ex.Message}");
        }
    }
}
