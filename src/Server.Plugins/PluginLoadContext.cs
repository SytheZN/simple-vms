using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace Server.Plugins;

public sealed class PluginLoadContext : AssemblyLoadContext
{
  private readonly AssemblyDependencyResolver _resolver;

  public PluginLoadContext(string pluginPath) : base(isCollectible: true)
  {
    _resolver = new AssemblyDependencyResolver(pluginPath);
  }

  [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Plugins are IL assemblies loaded by the JIT at runtime")]
  protected override Assembly? Load(AssemblyName assemblyName)
  {
    if (Default.Assemblies.Any(a => a.GetName().Name == assemblyName.Name))
      return null;

    var path = _resolver.ResolveAssemblyToPath(assemblyName);
    return path != null ? LoadFromAssemblyPath(path) : null;
  }

  protected override nint LoadUnmanagedDll(string unmanagedDllName)
  {
    var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
    return path != null ? LoadUnmanagedDllFromPath(path) : 0;
  }
}
