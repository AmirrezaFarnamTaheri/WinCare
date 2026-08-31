namespace WinCare.Application.Plugins;

using System.Reflection;
using System.Runtime.Versioning;

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
    public const string SupportedTargetFramework = "net8.0-windows10.0.19041.0";
    /// <summary>
    /// Loads a plugin assembly from the specified file path into an isolated collectible ALC.
    /// </summary>
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
            var targetFramework = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
            if (!string.Equals(targetFramework, ".NETCoreApp,Version=v8.0", StringComparison.Ordinal))
            {
                loadContext.Unload();
                return new AssemblyPluginLoadResult(false, null, null,
                    $"Plugin targets '{targetFramework ?? "an unknown framework"}'. Compiled plugins must target {SupportedTargetFramework}.");
            }

            var supportedOs = assembly.GetCustomAttribute<SupportedOSPlatformAttribute>()?.PlatformName;
            if (!string.Equals(supportedOs, "windows10.0.19041.0", StringComparison.OrdinalIgnoreCase))
            {
                loadContext.Unload();
                return new AssemblyPluginLoadResult(false, null, null,
                    $"Plugin is missing SupportedOSPlatform('windows10.0.19041.0'). Compiled plugins must target {SupportedTargetFramework}.");
            }

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
