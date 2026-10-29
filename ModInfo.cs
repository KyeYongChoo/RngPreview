namespace ModToggle;

/// <summary>This mod's identity for the shared toggle code in Shared/ModToggle.</summary>
public static class ModInfo
{
    public const string Id = "RngPreview";
    public const string Title = "RNG Preview";
    public const string LogPrefix = "[RngPreview]";

    public const string ToggleDescription =
        "When ticked, tooltips of random card and potion generators show their next roll, and F8 (or the RNG " +
        "button in combat) opens the planner. Takes effect immediately.";
}
