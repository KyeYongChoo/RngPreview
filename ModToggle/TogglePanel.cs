using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace ModToggle;

/// <summary>
/// The "Enabled" tickbox and a line about what it does. Built in code from stock Godot nodes plus the game's own
/// tickbox (mod assemblies can't register node scripts), so the vanilla mod list and BaseLib's Mod Configuration
/// menu can both host it.
/// </summary>
public sealed class TogglePanel
{
    private static readonly Color TextColor = new(1f, 0.964706f, 0.886275f);

    private readonly NTickbox _tickbox;
    private readonly Font? _font;
    private readonly Font? _boldFont;
    private readonly int _fontSize;

    /// <summary>The panel's root node; add it to any container.</summary>
    public Control Root { get; }

    public TogglePanel(int fontSize = 24)
    {
        _fontSize = fontSize;
        _font = LoadFont("res://themes/kreon_regular_shared.tres");
        _boldFont = LoadFont("res://themes/kreon_bold_shared.tres") ?? _font;

        VBoxContainer layout = new VBoxContainer { Name = ModInfo.Id + "TogglePanel" };
        layout.AddThemeConstantOverride("separation", 8);
        Root = layout;

        HBoxContainer header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        _tickbox = CreateGameTickbox();
        _tickbox.Connect(NTickbox.SignalName.Toggled, Callable.From<NTickbox>(tickbox => ToggleSettings.Enabled = tickbox.IsTicked));
        // IsTicked needs the tickbox's child nodes, which are only looked up once it enters the tree.
        _tickbox.Ready += () => _tickbox.IsTicked = ToggleSettings.Enabled;

        Label label = new Label
        {
            Text = "Enabled",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        StyleText(label, bold: true);
        // Clicking the label toggles too, like a normal checkbox.
        label.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            {
                _tickbox.ForceToggleTick();
            }
        };
        header.AddChild(_tickbox);
        header.AddChild(label);
        layout.AddChild(header);

        Label description = new Label
        {
            Text = ModInfo.ToggleDescription,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(560, 0),
        };
        StyleText(description, size: fontSize - 4);
        layout.AddChild(description);
    }

    /// <summary>Re-read the setting, e.g. after it was changed from the other menu.</summary>
    public void Refresh()
    {
        if (_tickbox.IsNodeReady())
        {
            _tickbox.IsTicked = ToggleSettings.Enabled; // setting IsTicked doesn't emit Toggled
        }
    }

    private void StyleText(Control control, bool bold = false, int? size = null)
    {
        Font? font = bold ? _boldFont : _font;
        if (font != null)
        {
            control.AddThemeFontOverride("font", font);
        }
        control.AddThemeFontSizeOverride("font_size", size ?? _fontSize);
        control.AddThemeColorOverride("font_color", TextColor);
    }

    private static Font? LoadFont(string path) => ResourceLoader.Exists(path) ? GD.Load<Font>(path) : null;

    /// <summary>
    /// The game's tickbox: an <see cref="NTickbox"/> with the visuals from its settings tickbox scene moved in
    /// (the scene itself has no script attached). Same approach BaseLib uses for its config tickboxes.
    /// </summary>
    private static NTickbox CreateGameTickbox()
    {
        NTickbox tickbox = new NTickbox
        {
            Name = ModInfo.Id + "EnabledTickbox",
            CustomMinimumSize = new Vector2(64, 64),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };

        Node source = GD.Load<PackedScene>("res://scenes/screens/settings_tickbox.tscn").Instantiate();
        foreach (Node child in source.GetChildren())
        {
            source.RemoveChild(child);
            // The controller selection reticle is laid out for the 320px-wide settings row; skip it.
            if (child.Name == "SelectionReticle")
            {
                child.QueueFree();
                continue;
            }
            tickbox.AddChild(child);
            SetOwnerRecursive(child, tickbox);
        }
        source.QueueFree();

        // NTickbox finds its visuals via the unique name %TickboxVisuals, which is resolved through the owner.
        tickbox.GetNode<Node>("TickboxVisuals").UniqueNameInOwner = true;
        return tickbox;
    }

    private static void SetOwnerRecursive(Node node, Node owner)
    {
        node.Owner = owner;
        foreach (Node child in node.GetChildren())
        {
            SetOwnerRecursive(child, owner);
        }
    }
}
