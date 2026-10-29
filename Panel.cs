using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using ModToggle;

namespace RngPreview;

/// <summary>
/// The planner window (F8, or the "RNG" button in combat). Built from plain Godot controls in code, since the mod
/// ships no scenes. Lives on its own CanvasLayer above the game.
/// </summary>
internal static class Panel
{
    private const int FontSize = 19;
    private const double RefreshSeconds = 0.4;

    private static CanvasLayer? _layer;
    private static Button _toggle = null!;
    private static Control _overlay = null!;
    private static VBoxContainer _content = null!;
    private static bool _f8WasDown;
    private static double _sinceRefresh;
    private static string _shownSignature = "";

    public static void Tick()
    {
        try
        {
            if (_layer == null || !GodotObject.IsInstanceValid(_layer))
            {
                Create();
            }
            // Turned off in the mod's settings: no button, no hotkey, and an open planner closes.
            Player? player = ToggleSettings.Enabled ? CurrentPlayer() : null;
            _toggle.Visible = player != null;
            bool f8 = Input.IsKeyPressed(Key.F8);
            if (f8 && !_f8WasDown && player != null)
            {
                Toggle();
            }
            _f8WasDown = f8;
            if (!_overlay.Visible)
            {
                return;
            }
            if (player == null)
            {
                _overlay.Visible = false;
                return;
            }
            _sinceRefresh += ((SceneTree)Engine.GetMainLoop()).Root.GetProcessDeltaTime();
            if (_sinceRefresh >= RefreshSeconds)
            {
                _sinceRefresh = 0;
                Refresh(player, force: false);
            }
        }
        catch (Exception e)
        {
            Log.Error("[RngPreview] " + e);
        }
    }

    private static Player? CurrentPlayer()
    {
        if (!CombatManager.Instance.IsInProgress)
        {
            return null;
        }
        IReadOnlyList<Player>? players = CombatManager.Instance.DebugOnlyGetState()?.Players;
        return players?.FirstOrDefault(p => LocalContext.IsMe(p)) ?? players?.FirstOrDefault();
    }

    private static void Toggle()
    {
        _overlay.Visible = !_overlay.Visible;
        if (_overlay.Visible && CurrentPlayer() is { } player)
        {
            Refresh(player, force: true);
        }
    }

    private static void Create()
    {
        _layer = new CanvasLayer { Layer = 90 };

        _toggle = new Button { Text = "RNG (F8)", FocusMode = Control.FocusModeEnum.None, Position = new Vector2(12, 150) };
        _toggle.AddThemeFontSizeOverride("font_size", 16);
        _toggle.Pressed += Toggle;
        _layer.AddChild(_toggle);

        // Full-screen dimmer that also swallows clicks meant for the combat underneath.
        ColorRect dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay = dim;
        _overlay.Visible = false;
        _layer.AddChild(_overlay);

        CenterContainer center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.AddChild(center);

        PanelContainer panel = new PanelContainer();
        StyleBoxFlat style = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.07f, 0.09f, 0.97f),
            BorderColor = new Color(0.55f, 0.45f, 0.25f),
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14,
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(8);
        panel.AddThemeStyleboxOverride("panel", style);
        center.AddChild(panel);

        VBoxContainer root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        panel.AddChild(root);

        HBoxContainer header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        Label title = MakeLabel("Random generation planner", 24, new Color(1f, 0.85f, 0.45f));
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        header.AddChild(MakeButton("Undo", () => { if (Planner.Plan.Count > 0) Planner.Plan.RemoveAt(Planner.Plan.Count - 1); RefreshNow(); }));
        header.AddChild(MakeButton("Clear plan", () => { Planner.Plan.Clear(); RefreshNow(); }));
        header.AddChild(MakeButton("Close (F8)", () => _overlay.Visible = false));
        root.AddChild(header);

        root.AddChild(MakeLabel(
            "Each option shows exactly what it gives if it is the next thing to roll, after your plan. Click an option to " +
            "add it to the plan; the rest update. The plan resets when the game rolls for real or the turn changes.",
            15, new Color(0.7f, 0.7f, 0.7f), wrap: true));

        ScrollContainer scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(1240, 660),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_content);
        root.AddChild(scroll);

        ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred(Node.MethodName.AddChild, _layer);
    }

    private static void RefreshNow()
    {
        if (CurrentPlayer() is { } player)
        {
            Refresh(player, force: true);
        }
    }

    private static void Refresh(Player player, bool force)
    {
        Planner.Validate(player);
        var (planned, next, nextCombat) = Planner.Evaluate(player);
        string signature = string.Join("|", planned.Concat(next).Select(x => x.step.Label + ":" + Format(x.rolls))
            .Concat(nextCombat.Select(x => x.card.Label + ":" + Format(x.rolls))));
        if (!force && signature == _shownSignature)
        {
            return;
        }
        _shownSignature = signature;

        foreach (Node child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        _content.AddChild(MakeLabel("Plan", 21, new Color(1f, 0.85f, 0.45f)));
        if (planned.Count == 0)
        {
            _content.AddChild(MakeLabel("  Nothing planned yet: the options below are what you'd get right now.", FontSize,
                new Color(0.6f, 0.6f, 0.6f)));
        }
        for (int i = 0; i < planned.Count; i++)
        {
            _content.AddChild(Row(new Label { Text = $"{i + 1}. {planned[i].step.Label}" }, planned[i].rolls));
        }

        _content.AddChild(new HSeparator());
        _content.AddChild(MakeLabel(planned.Count == 0 ? "Next" : "Next, after the plan", 21, new Color(1f, 0.85f, 0.45f)));
        foreach (var (step, rolls) in next)
        {
            Step chosen = step;
            Button button = MakeButton(step.Label, () => { Planner.Plan.Add(chosen); RefreshNow(); });
            Control row = Row(button, rolls);
            if (step.Spent)
            {
                row.Modulate = new Color(1, 1, 1, 0.5f); // all copies planned, still playable after a reshuffle
            }
            _content.AddChild(row);
        }

        if (nextCombat.Count == 0)
        {
            return;
        }
        // Cards out of this fight, grayed out: what each gives as the first roll of the next combat.
        _content.AddChild(new HSeparator());
        Color gray = new Color(1, 1, 1, 0.5f);
        Label header = MakeLabel("Not in this fight: next combat", 21, new Color(1f, 0.85f, 0.45f));
        header.Modulate = header.Modulate * gray;
        _content.AddChild(header);
        Label note = MakeLabel(
            "What each gives if it's the first thing to roll next combat, after your plan and the next combat's start " +
            "(Toolbox, Delicate Frond, ...). Power cards: what their power gives at the start of the turn after.",
            15, new Color(0.7f, 0.7f, 0.7f), wrap: true);
        note.Modulate = note.Modulate * gray;
        _content.AddChild(note);
        foreach (var (card, rolls) in nextCombat)
        {
            Control row = Row(new Label { Text = card.Label }, rolls);
            row.Modulate = gray;
            _content.AddChild(row);
        }
    }

    private static Control Row(Control left, List<Roll> rolls)
    {
        HBoxContainer row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        left.CustomMinimumSize = new Vector2(430, 0);
        left.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        if (left is Label label)
        {
            label.AddThemeFontSizeOverride("font_size", FontSize);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        row.AddChild(left);
        RichTextLabel text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = Format(rolls),
        };
        text.AddThemeFontSizeOverride("normal_font_size", FontSize);
        text.AddThemeFontSizeOverride("italics_font_size", FontSize - 2);
        row.AddChild(text);
        return row;
    }

    private static string Format(List<Roll> rolls)
    {
        if (rolls.Count == 0)
        {
            return "[color=#808080]nothing rolls[/color]";
        }
        bool labelled = rolls.Count > 1 || rolls[0].Note != null;
        return string.Join("\n", rolls.Select(r =>
        {
            string items = r.Items.Count == 0
                ? (r.Note == null ? "[color=#808080]nothing[/color]" : "")
                : string.Join(", ", r.Items.Select(i => $"[color={RarityColor(i.Rarity)}]{i.Name}[/color]"));
            string head = labelled ? $"[color=#c8b07a]{r.Label}:[/color] " : "";
            string note = r.Note == null ? "" : $" [i][color=#909090]({r.Note})[/color][/i]";
            return head + items + note;
        }));
    }

    private static string RarityColor(object rarity) => rarity.ToString() switch
    {
        "Uncommon" => "#6fb6ff",
        "Rare" => "#ffcf40",
        "Ancient" or "Event" => "#e08aff",
        _ => "#e6e6e6",
    };

    private static Label MakeLabel(string text, int size, Color color, bool wrap = false)
    {
        Label label = new Label { Text = text, Modulate = color };
        label.AddThemeFontSizeOverride("font_size", size);
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(1240, 0);
        }
        return label;
    }

    private static Button MakeButton(string text, Action pressed)
    {
        Button button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
        button.AddThemeFontSizeOverride("font_size", FontSize - 1);
        button.Pressed += pressed;
        return button;
    }
}
