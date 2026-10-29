using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;

namespace ModToggle;

/// <summary>
/// Hosts the <see cref="TogglePanel"/> in Settings > Mods when this mod is selected, in the mod image slot of
/// <see cref="NModInfoContainer"/> (this mod has no image).
/// </summary>
public static class ModSettingsHost
{
    private static string HostName => ModInfo.Id + "Settings";

    // Live panels keyed by their host node; a new modding screen instance creates a new panel.
    private static readonly Dictionary<Control, TogglePanel> Panels = new();

    private static void SetVisible(NModInfoContainer container, bool visible)
    {
        Control? host = container.GetNodeOrNull<Control>(HostName);
        if (host == null)
        {
            if (!visible)
            {
                return;
            }
            host = new MarginContainer
            {
                Name = HostName,
                Position = new Vector2(22, 104),
                Size = new Vector2(620, 380),
                MouseFilter = Control.MouseFilterEnum.Pass,
            };
            TogglePanel panel = new TogglePanel();
            host.AddChild(panel.Root);
            Control created = host;
            host.TreeExiting += () => Panels.Remove(created);
            container.AddChild(host);
            Panels[host] = panel;
        }
        else if (visible && Panels.TryGetValue(host, out TogglePanel? panel))
        {
            panel.Refresh();
        }
        host.Visible = visible;
    }

    [HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer.Fill))]
    private static class FillPatch
    {
        private static void Postfix(NModInfoContainer __instance, Mod mod)
        {
            try
            {
                SetVisible(__instance, mod.manifest?.id == ModInfo.Id);
            }
            catch (Exception e)
            {
                Log.Error($"{ModInfo.LogPrefix} Failed to build mod settings panel: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer.Clear))]
    private static class ClearPatch
    {
        private static void Postfix(NModInfoContainer __instance)
        {
            try
            {
                SetVisible(__instance, false);
            }
            catch (Exception e)
            {
                Log.Error($"{ModInfo.LogPrefix} Failed to hide mod settings panel: {e}");
            }
        }
    }
}
