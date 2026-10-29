using System.Reflection;
using System.Runtime.Loader;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace ModToggle;

/// <summary>
/// Optional integration with BaseLib's "Mod Configuration" menu.
/// The BaseLib-specific code lives in a separate assembly (&lt;ModId&gt;.BaseLib.dll), because the game calls
/// GetTypes() on this one and would fail to load the whole mod if any type here referenced BaseLib while BaseLib isn't
/// installed.
/// </summary>
public static class BaseLibBridge
{
    private const string BaseLibModId = "BaseLib";

    private static string CompatAssemblyName => ModInfo.Id + ".BaseLib";

    private static string IntegrationType => ModInfo.Id + ".BaseLibCompat.BaseLibIntegration";

    /// <summary>
    /// Mod load order only follows declared dependencies (and the player's manual ordering), so BaseLib may load
    /// after this mod. Check once every mod has finished loading; BaseLib only reads its registry when its menu opens.
    /// </summary>
    public static void RegisterWhenModsLoaded() => Callable.From(TryRegister).CallDeferred();

    private static void TryRegister()
    {
        try
        {
            if (!ModManager.GetLoadedMods().Any(m => m.manifest?.id == BaseLibModId))
            {
                return;
            }
            Assembly self = typeof(BaseLibBridge).Assembly;
            string path = Path.Combine(Path.GetDirectoryName(self.Location)!, CompatAssemblyName + ".dll");
            if (!File.Exists(path))
            {
                Log.Error($"{ModInfo.LogPrefix} BaseLib is loaded but {CompatAssemblyName}.dll is missing; skipping Mod Configuration entry");
                return;
            }
            AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(self) ?? AssemblyLoadContext.Default;
            Assembly compat = context.LoadFromAssemblyPath(path);
            compat.GetType(IntegrationType, throwOnError: true)!
                .GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null);
            Log.Info($"{ModInfo.LogPrefix} Registered with BaseLib Mod Configuration");
        }
        catch (Exception e)
        {
            Log.Error($"{ModInfo.LogPrefix} Failed to register with BaseLib: {e}");
        }
    }
}
