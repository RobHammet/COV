// DebugMenu.cs — in-game debug panel (F1 to toggle).
// Only active when Globals.showDebugTools is true.
//
// Sections:
//   SCENES  — one button per scene; teleports immediately.
//   ITEMS   — one toggle button per InventoryItem.ItemType (except none).
//
// Built entirely in code; no .tscn required. Add as a child of DebugLayer.

using Godot;
using System;
using System.Linq;

public partial class DebugMenu : Control
{
    public MainScene mainScene;

    private Panel           _bg;
    private VBoxContainer   _itemsBox;       // refreshed each toggle-open
    private VBoxContainer   _thingStatesBox; // refreshed each toggle-open

    public override void _Ready()
    {
        // Full-screen anchor so the panel can be placed freely.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Semi-transparent background panel.
        _bg = new Panel();
        _bg.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterRight);
        _bg.Size        = new Vector2(260, 540);
        _bg.Position    = new Vector2(-268, -270);   // right-edge, vertically centred
        _bg.MouseFilter = MouseFilterEnum.Stop;

        var style = new StyleBoxFlat
        {
            BgColor                  = new Color(0.05f, 0.05f, 0.1f, 0.92f),
            BorderColor              = new Color(0.4f, 0.6f, 1f, 0.8f),
            BorderWidthLeft          = 2,
            BorderWidthRight         = 2,
            BorderWidthTop           = 2,
            BorderWidthBottom        = 2,
            CornerRadiusTopLeft      = 6,
            CornerRadiusTopRight     = 6,
            CornerRadiusBottomLeft   = 6,
            CornerRadiusBottomRight  = 6,
        };
        _bg.AddThemeStyleboxOverride("panel", style);
        AddChild(_bg);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetLeft = 8; scroll.OffsetTop = 8;
        scroll.OffsetRight = -8; scroll.OffsetBottom = -8;
        _bg.AddChild(scroll);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(root);

        // ── SCENES ──────────────────────────────────────────────
        root.AddChild(MakeHeader("SCENES"));
        foreach (var (name, path) in Scenes.All())
        {
            string capName = name.ToUpper();
            string capPath = path;   // captured for lambda
            var btn = MakeButton(capName);
            btn.Pressed += () =>
            {
                Hide();
                mainScene.ChangeSceneToFile(capPath);
            };
            root.AddChild(btn);
        }

        // ── ITEMS ────────────────────────────────────────────────
        root.AddChild(MakeSpacer());
        root.AddChild(MakeHeader("ITEMS"));
        _itemsBox = new VBoxContainer();
        _itemsBox.AddThemeConstantOverride("separation", 4);
        root.AddChild(_itemsBox);

        // ── THING STATES ─────────────────────────────────────────
        root.AddChild(MakeSpacer());
        root.AddChild(MakeHeader("THING STATES"));
        _thingStatesBox = new VBoxContainer();
        _thingStatesBox.AddThemeConstantOverride("separation", 2);
        root.AddChild(_thingStatesBox);

        Hide();
    }

    public void Toggle()
    {
        if (Visible) { Hide(); return; }
        RefreshItems();
        RefreshThingStates();
        Show();
    }

    private void RefreshThingStates()
    {
        foreach (Node c in _thingStatesBox.GetChildren()) c.QueueFree();

        string lastScene = null;
        foreach (var (scene, thing, summary) in mainScene.GetThingStateDebugLines())
        {
            if (scene != lastScene)
            {
                if (lastScene != null) _thingStatesBox.AddChild(MakeSpacer());
                var sceneLbl = new Label { Text = scene.ToUpper() };
                sceneLbl.AddThemeColorOverride("font_color",    new Color(0.8f, 0.7f, 1f));
                sceneLbl.AddThemeFontSizeOverride("font_size",  11);
                _thingStatesBox.AddChild(sceneLbl);
                lastScene = scene;
            }
            var lbl = new Label { Text = $"  {thing}  {summary}" };
            lbl.AddThemeColorOverride("font_color",   new Color(0.75f, 0.9f, 0.75f));
            lbl.AddThemeFontSizeOverride("font_size", 11);
            _thingStatesBox.AddChild(lbl);
        }

        if (lastScene == null)
        {
            var empty = new Label { Text = "  (none)" };
            empty.AddThemeColorOverride("font_color",   new Color(0.5f, 0.5f, 0.5f));
            empty.AddThemeFontSizeOverride("font_size", 11);
            _thingStatesBox.AddChild(empty);
        }
    }

    private void RefreshItems()
    {
        foreach (Node c in _itemsBox.GetChildren()) c.QueueFree();

        foreach (InventoryItem.ItemType type in Enum.GetValues<InventoryItem.ItemType>())
        {
            if (type == InventoryItem.ItemType.none) continue;
            bool has = mainScene.inventory.Any(i => i.Type == type);
            var btn = MakeButton((has ? "[x] " : "[ ] ") + type.ToString().ToUpper());
            if (has)
                btn.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
            var captured = type;
            btn.Pressed += () =>
            {
                if (mainScene.inventory.Any(i => i.Type == captured))
                    mainScene.inventory.RemoveAll(i => i.Type == captured);
                else
                    mainScene.inventory.Add(new InventoryItem(captured));
                RefreshItems();
            };
            _itemsBox.AddChild(btn);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static Label MakeHeader(string text)
    {
        var lbl = new Label { Text = text };
        lbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1f));
        lbl.AddThemeFontSizeOverride("font_size", 13);
        return lbl;
    }

    private static Button MakeButton(string text)
    {
        var btn = new Button { Text = text };
        btn.Flat = true;
        btn.Alignment = HorizontalAlignment.Left;
        btn.AddThemeColorOverride("font_color",         new Color(0.9f, 0.9f, 0.9f));
        btn.AddThemeColorOverride("font_hover_color",   new Color(1f,   1f,   0.4f));
        btn.AddThemeColorOverride("font_pressed_color", new Color(1f,   0.6f, 0.2f));
        return btn;
    }

    private static Control MakeSpacer()
    {
        var s = new Control(); s.CustomMinimumSize = new Vector2(0, 6); return s;
    }
}
