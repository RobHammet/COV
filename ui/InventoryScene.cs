// InventoryScene.cs — camera-independent inventory overlay.
//
// Lives on a CanvasLayer (layer 12) so camera zoom/pan in the scene has no
// effect on its layout. Spawned once by MainScene and reused across scene
// changes. Opened/closed by scene_script.ToggleInventory().
//
// The scene is NOT paused while the inventory is open — the scene's event
// queue keeps ticking so inventory-triggered speech plays. Scene input is
// blocked instead by the inventoryScene.Visible check in _UnhandledInput.
//
// Verb dispatch:
//   walk  → select item (set usingItem, update verb tray icon)
//   look/use/talk/item → execute actions in-place (inventory stays open)
//
// Speech actions in inventory scripts are routed through _invAnchor — a
// DialogAnchor placed just below the panel — with NoTail style. This keeps
// dialogs visible below the panel without requiring ego to be in the scene.
//
// Clicking outside the panel closes the inventory without triggering any
// scene action regardless of the current interact mode.

using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

public partial class InventoryScene : CanvasLayer
{
    private const int   COLS      = 4;
    private const int   ROWS      = 4;
    private const float CELL_SIZE = 64f;
    private const float CELL_PAD  = 10f;
    private const float PANEL_PAD = 18f;
    private const float CORNER_R  = 14f;

    private MainScene    _mainScene;
    private JsonObject   _data;
    private DialogAnchor _invAnchor;

    private readonly List<Slot> _slots = new();
    private Rect2  _panelRect;
    private Panel  _panelBg;

    private float   _holdTimer     = 0f;
    private const float HoldThreshold = 0.35f;
    private Slot    _holdSlot;
    private Vector2 _holdPos;
    private bool    _holdTriggered;
    private bool    _pressActive;

    private class Slot
    {
        public InventoryItem.ItemType Type;
        public Rect2                  Bounds;
        public Node2D                 Node;
        public Sprite2D               Sprite;
        public JsonObject             Data;
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        Layer      = 12;
        _mainScene = GetTree().Root.GetChild(1) as MainScene;

        const string path = "res://game/data/inventory.covscript";
        if (FileAccess.FileExists(path))
            _data = CovScript.Parse(FileAccess.GetFileAsString(path));

        _invAnchor        = new DialogAnchor();
        _invAnchor.Facing = NPC.Direction.up;
        AddChild(_invAnchor);

        Hide();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        if (NeedsRebuild()) BuildPanel();

        if (_holdSlot != null && !_holdTriggered && _mainScene?.currentScene?.verbCoinControl == null)
        {
            _holdTimer += (float)delta;
            if (_holdTimer >= HoldThreshold)
            {
                _holdTriggered = true;
                var slot = _holdSlot;
                _mainScene.currentScene.ShowInventoryVerbCoin(
                    slot.Bounds.GetCenter(),
                    slot.Type,
                    mode => DispatchVerb(slot, mode),
                    onCleanup: ResetHoldState);
            }
        }
    }

    private void ResetHoldState()
    {
        _pressActive   = false;
        _holdSlot      = null;
        _holdTimer     = 0f;
        _holdTriggered = false;
    }

    // -------------------------------------------------------------------------
    // Open / Close
    // -------------------------------------------------------------------------

    public void Open()
    {
        ResetHoldState();
        BuildPanel();
        Show();
    }

    public void Close()
    {
        ResetHoldState();
        Hide();
        ClearSlots();
    }

    // -------------------------------------------------------------------------
    // Panel construction
    // -------------------------------------------------------------------------

    private void BuildPanel()
    {
        ClearSlots();

        var   vp      = GetViewport().GetVisibleRect().Size;
        float panelW  = COLS * (CELL_SIZE + CELL_PAD) + CELL_PAD + PANEL_PAD * 2;
        float panelH  = ROWS * (CELL_SIZE + CELL_PAD) + CELL_PAD + PANEL_PAD * 2;
        float panelX  = Mathf.Round((vp.X - panelW) / 2f);
        float panelY  = Mathf.Round((vp.Y - panelH) / 2f);
        _panelRect    = new Rect2(panelX, panelY, panelW, panelH);

        // Anchor sits below the panel so inventory speech appears there, notail.
        _invAnchor.Position = new Vector2(_panelRect.GetCenter().X, _panelRect.End.Y + 100f);

        if (_panelBg == null)
        {
            var style = new StyleBoxFlat
            {
                BgColor                = new Color(0.13f, 0.10f, 0.08f, 0.94f),
                BorderColor            = new Color(0.42f, 0.33f, 0.20f, 1f),
                BorderWidthTop         = 2,
                BorderWidthBottom      = 2,
                BorderWidthLeft        = 2,
                BorderWidthRight       = 2,
                CornerRadiusTopLeft     = (int)CORNER_R,
                CornerRadiusTopRight    = (int)CORNER_R,
                CornerRadiusBottomLeft  = (int)CORNER_R,
                CornerRadiusBottomRight = (int)CORNER_R,
            };
            _panelBg = new Panel();
            _panelBg.AddThemeStyleboxOverride("panel", style);
            AddChild(_panelBg);
        }

        _panelBg.Position = _panelRect.Position;
        _panelBg.Size     = _panelRect.Size;
        _panelBg.ZIndex   = 0;

        var items = _mainScene.inventory;
        for (int i = 0; i < items.Count && i < COLS * ROWS; i++)
            _slots.Add(BuildSlot(items[i].Type, i));

        UpdateSelectionVisuals();
    }

    private void UpdateSelectionVisuals()
    {
        bool hasSelection = _mainScene.GetInteractMode() == Globals.InteractModes.item;
        foreach (var s in _slots)
            s.Sprite.Modulate = (hasSelection && s.Type == _mainScene.usingItem)
                ? new Color(1f, 0.85f, 0.2f)
                : Colors.White;
    }

    private Slot BuildSlot(InventoryItem.ItemType type, int index)
    {
        int   col    = index % COLS;
        int   row    = index / COLS;
        float x      = _panelRect.Position.X + PANEL_PAD + CELL_PAD + col * (CELL_SIZE + CELL_PAD);
        float y      = _panelRect.Position.Y + PANEL_PAD + CELL_PAD + row * (CELL_SIZE + CELL_PAD);
        var   bounds = new Rect2(x, y, CELL_SIZE, CELL_SIZE);

        var node   = new Node2D { Position = bounds.GetCenter(), ZIndex = 1 };
        var sprite = new Sprite2D { Texture = GetItemAtlasTexture(type) };

        if (sprite.Texture != null)
        {
            float scale = CELL_SIZE * 0.82f / Mathf.Max(sprite.Texture.GetWidth(), sprite.Texture.GetHeight());
            sprite.Scale = new Vector2(scale, scale);
        }

        node.AddChild(sprite);
        AddChild(node);

        return new Slot
        {
            Type   = type,
            Bounds = bounds,
            Node   = node,
            Sprite = sprite,
            Data   = _data?["things"]?[type.ToString().ToLower()]?.AsObject(),
        };
    }

    private AtlasTexture GetItemAtlasTexture(InventoryItem.ItemType type)
    {
        var cursor = _mainScene?.cursor;
        if (cursor?.itemTexture == null) return null;

        int numCols = cursor.itemTextureSize.X;
        int numRows = cursor.itemTextureSize.Y;
        int cellW   = cursor.itemTexture.GetWidth()  / numCols;
        int cellH   = cursor.itemTexture.GetHeight() / numRows;
        int idx     = (int)type - 1;
        int col     = idx % numCols;
        int row     = idx / numCols;

        return new AtlasTexture
        {
            Atlas  = cursor.itemTexture,
            Region = new Rect2(col * cellW, row * cellH, cellW, cellH),
        };
    }

    private void ClearSlots()
    {
        foreach (var s in _slots) s.Node?.QueueFree();
        _slots.Clear();
    }

    // Rebuilds the grid if the live inventory no longer matches what's displayed.
    private bool NeedsRebuild()
    {
        var items = _mainScene.inventory;
        if (items.Count != _slots.Count) return true;
        for (int i = 0; i < items.Count; i++)
            if (items[i].Type != _slots[i].Type) return true;
        return false;
    }

    // -------------------------------------------------------------------------
    // Input
    // -------------------------------------------------------------------------

    public override void _Input(InputEvent @event)
    {
        if (!Visible) return;
        if (_mainScene?.currentScene?.eventQueue?.isRunning == true) return;
        if (_mainScene?.currentScene?.verbCoinControl != null) return;

        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.I)
        {
            Close();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right && mb.Pressed)
        {
            _mainScene?.currentScene?.AdvanceInteractMode();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Unified press/release for mouse-left and touch.
        // _pressActive deduplicates: with emulate_touch_from_mouse both events fire;
        // whichever arrives first wins and the second is swallowed.
        bool isPress   = (@event is InputEventMouseButton mp && mp.ButtonIndex == MouseButton.Left && mp.Pressed)
                      || (@event is InputEventScreenTouch  tp && tp.Pressed);
        bool isRelease = (@event is InputEventMouseButton mr && mr.ButtonIndex == MouseButton.Left && !mr.Pressed)
                      || (@event is InputEventScreenTouch  tr && !tr.Pressed);

        Vector2 pos = @event is InputEventMouseButton mpos ? mpos.Position
                    : @event is InputEventScreenTouch tpos ? tpos.Position
                    : Vector2.Zero;

        if (isPress)
        {
            if (_pressActive) { GetViewport().SetInputAsHandled(); return; }
            _pressActive   = true;
            _holdPos       = pos;
            _holdSlot      = HitTestSlot(pos);
            _holdTimer     = 0f;
            _holdTriggered = false;
            GetViewport().SetInputAsHandled();
        }
        else if (isRelease)
        {
            if (!_pressActive) { GetViewport().SetInputAsHandled(); return; }
            _pressActive = false;

            bool triggered = _holdTriggered;
            _holdSlot      = null;
            _holdTimer     = 0f;
            _holdTriggered = false;

            if (!triggered)
                HandlePrimaryPress(_holdPos);

            GetViewport().SetInputAsHandled();
        }
    }

    private void HandlePrimaryPress(Vector2 pos)
    {
        if (!_panelRect.HasPoint(pos))
        {
            Close();
            GetViewport().SetInputAsHandled();
            return;
        }

        var slot = HitTestSlot(pos);
        if (slot != null)
        {
            // On mobile, tap always selects (walk). Verb coin handles other modes.
            var mode = (_mainScene.currentInputMode == Globals.InputModes.verbcoin)
                ? Globals.InteractModes.walk
                : _mainScene.GetInteractMode();
            DispatchVerb(slot, mode);
        }

        GetViewport().SetInputAsHandled();
    }

    private Slot HitTestSlot(Vector2 pos)
    {
        foreach (var s in _slots)
            if (s.Bounds.HasPoint(pos)) return s;
        return null;
    }

    // -------------------------------------------------------------------------
    // Verb dispatch
    // -------------------------------------------------------------------------

    private void DispatchVerb(Slot slot, Globals.InteractModes mode)
    {
        if (mode == Globals.InteractModes.walk)
        {
            SelectItem(slot.Type);
            return;
        }

        JsonArray actions   = null;
        string[]  fallbacks = null;

        switch (mode)
        {
            case Globals.InteractModes.look:
                actions = slot.Data?["look"]?.AsArray();
                break;
            case Globals.InteractModes.use:
                actions   = slot.Data?["use"]?.AsArray();
                fallbacks = thing.UseFallbacks;
                break;
            case Globals.InteractModes.talk:
                actions   = slot.Data?["talk"]?.AsArray();
                fallbacks = thing.TalkFallbacks;
                break;
            case Globals.InteractModes.item:
                string key = _mainScene.usingItem.ToString().ToLower();
                actions   = slot.Data?["use_item"]?[key]?.AsArray();
                fallbacks = thing.UseItemFallbacks;
                break;
        }

        if (actions != null)
        {
            ExecuteActionsInPlace(actions);
        }
        else if (fallbacks != null && fallbacks.Length > 0)
        {
            string fb = fallbacks[(int)(GD.Randf() * fallbacks.Length)];
            QueueSpeakViaAnchor(fb);
        }
    }

    private void SelectItem(InventoryItem.ItemType type)
    {
        _mainScene.usingItem = type;
        _mainScene.SetInteractMode(Globals.InteractModes.item);
        _mainScene.SetItemButtonIcon(GetItemImageTexture(type));
        UpdateSelectionVisuals();
    }

    // -------------------------------------------------------------------------
    // Action execution — stays in inventory, speech routed via anchor
    // -------------------------------------------------------------------------

    // Runs actions without closing the inventory. Speech is routed through
    // _invAnchor (below the panel) with NoTail so dialogs appear outside the
    // panel. Non-speech actions (set_flag, wait, obtain, remove, etc.) are
    // dispatched to the current scene's event queue via ScriptParser.
    private void ExecuteActionsInPlace(JsonArray actions)
    {
        var scene = _mainScene.currentScene;
        if (scene == null) return;

        var patched  = PatchInventoryActions(actions);
        var deferred = new JsonArray();

        foreach (var node in patched)
        {
            var obj    = node?.AsObject();
            if (obj == null) continue;
            string act = obj["action"]?.GetValue<string>() ?? "";

            switch (act)
            {
                case "speak": {
                    FlushDeferred(scene, deferred);
                    string text = obj["text"]?.GetValue<string>() ?? "";
                    var (dtype, _) = ScriptParser.ParseSpeakStyle(obj["style"]?.GetValue<string>());
                    scene.eventQueue.AddEventSpeakInCorner(text, dtype,
                        Globals.NarrationCorner.BottomCenter,
                        color: _mainScene.currentScene?.ego?.dialogColor);
                    break;
                }
                case "think": {
                    FlushDeferred(scene, deferred);
                    string text = obj["text"]?.GetValue<string>() ?? "";
                    scene.eventQueue.AddEventSpeakInCorner(text, Globals.DialogTypes.thinking,
                        Globals.NarrationCorner.BottomCenter,
                        color: _mainScene.currentScene?.ego?.dialogColor);
                    break;
                }
                case "narrate": {
                    FlushDeferred(scene, deferred);
                    deferred.Add(JsonNode.Parse(node.ToJsonString()));
                    FlushDeferred(scene, deferred);
                    break;
                }
                default:
                    deferred.Add(JsonNode.Parse(node.ToJsonString()));
                    break;
            }
        }

        FlushDeferred(scene, deferred);
    }

    private static void FlushDeferred(scene_script scene, JsonArray deferred)
    {
        if (deferred.Count == 0) return;
        ScriptParser.PopulateEventQueue(scene.eventQueue, deferred, scene, null);
        while (deferred.Count > 0) deferred.RemoveAt(0);
    }

    // Queues a single speak/think line via the anchor without any other actions.
    // Used for fallback responses.
    private void QueueSpeakViaAnchor(string text)
    {
        var scene = _mainScene.currentScene;
        if (scene == null) return;
        scene.eventQueue.AddEventSpeakInCorner(text, Globals.DialogTypes.thinking,
            Globals.NarrationCorner.BottomCenter,
            color: scene.ego?.dialogColor);
    }

    // Rewrites inventory-specific actions so ScriptParser handles them correctly:
    //   remove itemname  → remove_inv_item  (removes from inventory list + resets usingItem)
    //   obtain itemname  → add_inv_item     (adds directly to inventory list by type name)
    private JsonArray PatchInventoryActions(JsonArray actions)
    {
        var result = new JsonArray();
        foreach (var node in actions)
        {
            var obj = node?.AsObject();
            if (obj == null) { result.Add(node?.DeepClone()); continue; }

            string action = obj["action"]?.GetValue<string>() ?? "";

            if (action == "toggle_exist" && obj["value"]?.GetValue<bool>() == false)
            {
                string target = obj["target"]?.GetValue<string>() ?? "";
                if (Enum.TryParse<InventoryItem.ItemType>(target, true, out var t) &&
                    t != InventoryItem.ItemType.none)
                {
                    result.Add(new JsonObject { ["action"] = "remove_inv_item", ["item"] = target });
                    continue;
                }
            }

            if (action == "add_to_inventory" && obj.ContainsKey("item"))
            {
                result.Add(new JsonObject { ["action"] = "add_inv_item", ["item"] = obj["item"].GetValue<string>() });
                continue;
            }

            result.Add(JsonNode.Parse(node.ToJsonString()));
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // Item button icon helpers
    // -------------------------------------------------------------------------

    private ImageTexture GetItemImageTexture(InventoryItem.ItemType type)
    {
        if (type == InventoryItem.ItemType.none) return null;
        var cursor = _mainScene?.cursor;
        if (cursor?.itemTexture == null) return null;

        int numCols = cursor.itemTextureSize.X;
        int numRows = cursor.itemTextureSize.Y;
        var img     = cursor.itemTexture.GetImage();
        int cellW   = img.GetWidth()  / numCols;
        int cellH   = img.GetHeight() / numRows;
        int idx     = (int)type - 1;
        int col     = idx % numCols;
        int row     = idx / numCols;

        var cell = Image.CreateEmpty(cellW, cellH, false, img.GetFormat());
        cell.BlitRect(img, new Rect2I(col * cellW, row * cellH, cellW, cellH), Vector2I.Zero);
        var tex = new ImageTexture();
        tex.SetImage(cell);
        return tex;
    }
}
