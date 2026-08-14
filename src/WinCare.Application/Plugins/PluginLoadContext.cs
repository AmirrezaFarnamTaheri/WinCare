namespace WinCare.Application.Plugins;

using System.Reflection;
using System.Runtime.Loader;

/// <summary>
/// Custom AssemblyLoadContext for loading compiled C# plugin assemblies in isolation.
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginAssemblyPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name != null)
        {
            if (assemblyName.Name.StartsWith("WinCare.", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.Name.StartsWith("Microsoft.UI.Xaml", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.Name.StartsWith("Microsoft.WindowsAppRuntime", StringComparison.OrdinalIgnoreCase))
            {
                return null; // Delegate shared host assemblies to Default AssemblyLoadContext
            }
        }

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}
