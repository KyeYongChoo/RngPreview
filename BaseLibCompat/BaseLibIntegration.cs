using BaseLib.Config;
using BaseLib.Config.UI;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using ModToggle;

namespace RngPreview.BaseLibCompat;

/// <summary>
/// Adds this mod to BaseLib's home screen "Mod Configuration" menu. Only loaded by <see cref="BaseLibBridge"/> when
/// BaseLib is installed.
/// </summary>
public static class BaseLibIntegration
{
    // BaseLib looks up "<ROOT NAMESPACE>.mod_title" in settings_ui and otherwise shows the raw namespace.
    private const string TitleKey = "RNGPREVIEW.mod_title";

    public static void Register()
    {
        AddTitle();
        // Loc tables are rebuilt on language change, so re-add the title whenever the menu builds its mod list.
        var initializeModList = AccessTools.Method(typeof(NModConfigSubmenu), "InitializeModList");
        if (initializeModList != null)
        {
            new Harmony("KyeYongChoo." + ModInfo.Id + ".baselib")
                .Patch(initializeModList, prefix: new HarmonyMethod(typeof(BaseLibIntegration), nameof(AddTitle)));
        }
        ModConfigRegistry.Register(ModInfo.Id, new ToggleModConfig());
    }

    private static void AddTitle()
    {
        try
        {
            LocManager.Instance?.GetTable("settings_ui").MergeWith(new Dictionary<string, string> { [TitleKey] = ModInfo.Title });
        }
        catch (Exception e)
        {
            Log.Error($"{ModInfo.LogPrefix} Could not set Mod Configuration title: {e.Message}");
        }
    }
}

/// <summary>
/// The setting lives in <see cref="ToggleSettings"/> (shared with the vanilla mod list panel), so this config has no
/// BaseLib-managed properties; it only supplies the custom UI.
/// </summary>
public sealed class ToggleModConfig : ModConfig
{
    public ToggleModConfig() : base(ModInfo.Id)
    {
    }

    public override bool VisibleInModList() => true;

    public override void SetupConfigUI(Control optionContainer)
    {
        TogglePanel panel = new TogglePanel(fontSize: 28);
        panel.Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        optionContainer.AddChild(panel.Root);
    }
}
