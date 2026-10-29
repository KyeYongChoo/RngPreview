using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using ModToggle;

namespace RngPreview;

[ModInitializer(nameof(Init))]
internal static class Mod
{
    private static void Init()
    {
        ToggleSettings.Load();
        new Harmony("KyeYongChoo.RngPreview").PatchAll(typeof(Mod).Assembly);
        BaseLibBridge.RegisterWhenModsLoaded();
        ((SceneTree)Engine.GetMainLoop()).ProcessFrame += Panel.Tick;
    }
}
