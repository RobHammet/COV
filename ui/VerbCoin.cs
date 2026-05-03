using System;
using Godot;

public partial class VerbCoin : Control
{
    public scene_script parentScene;

    // Set before AddChild when spawning from inventory context.
    public bool                           showInventoryButton = true;
    public InventoryItem.ItemType         targetSlotType      = InventoryItem.ItemType.none;
    public Action<Globals.InteractModes>  OnCommit;
    public Action                         OnClose;

    private Node2D        _node2d;
    private TextureButton _lookButton;
    private TextureButton _talkButton;
    private TextureButton _useButton;
    private TextureButton _itemButton;
    private TextureButton _inventoryButton;

    private TextureButton[] _buttons;
    private Vector2[]       _buttonCenters;
    private Vector2[]       _buttonHalfSizes;

    private Sprite2D    _hubIcon;
    private const float HubRadius        = 30f;
    private const float SpokeCircleRadius = 40f;
    private const float OpenDuration     = 0.18f;
    private const float CloseDuration    = 0.14f;
    private const float ConfirmDelay     = 0.5f;
    private const float HighlightScale   = 1.2f;

    private bool _confirming              = false;
    private bool _closing                = false;
    private bool _requirePressBeforeCommit = false;
    private int  _highlightedIdx          = -1;

    public override void _Ready()
    {
        parentScene ??= GetParentOrNull<scene_script>();
        _requirePressBeforeCommit = OnCommit != null;
        _node2d = GetNode<Node2D>("VerbCoin_Node2D");

        _lookButton      = _node2d.GetNode<TextureButton>("LookButton");
        _talkButton      = _node2d.GetNode<TextureButton>("TalkButton");
        _useButton       = _node2d.GetNode<TextureButton>("UseButton");
        _itemButton      = _node2d.GetNode<TextureButton>("ItemButton");
        _inventoryButton = _node2d.GetNode<TextureButton>("InventoryButton");

        if (!showInventoryButton) _inventoryButton.Hide();
        _buttons = showInventoryButton
            ? [_lookButton, _talkButton, _useButton, _itemButton, _inventoryButton]
            : [_lookButton, _talkButton, _useButton, _itemButton];

        // Cache natural center and half-size; set pivot to center for highlight scaling
        _buttonCenters   = new Vector2[_buttons.Length];
        _buttonHalfSizes = new Vector2[_buttons.Length];
        for (int i = 0; i < _buttons.Length; i++)
        {
            var b = _buttons[i];
            _buttonHalfSizes[i] = new Vector2(
                (b.OffsetRight - b.OffsetLeft) * 0.5f,
                (b.OffsetBottom - b.OffsetTop) * 0.5f);
            _buttonCenters[i] = new Vector2(
                (b.OffsetLeft + b.OffsetRight)  * 0.5f,
                (b.OffsetTop  + b.OffsetBottom) * 0.5f);
            b.PivotOffset = _buttonHalfSizes[i];
        }

        ClampButtonsToInnerRect();

        // Hub icon sprite — at Node2D origin, above buttons, hidden until selection
        _hubIcon = new Sprite2D { ZIndex = 10, ZAsRelative = true, Visible = false };
        _node2d.AddChild(_hubIcon);

        ZIndex = _node2d.ZIndex;

        // Sync item button; show default (none) icon for self-use
        var defaultItemTex = _itemButton.TextureNormal;
        var verbPanel = parentScene.mainScene.overlayScene?.verbPanel;
        if (verbPanel != null)
            _itemButton.TextureNormal = verbPanel.itemButton.TextureNormal;
        bool selfUse = targetSlotType != InventoryItem.ItemType.none
                       && targetSlotType == parentScene.mainScene.usingItem;
        if (selfUse) _itemButton.TextureNormal = defaultItemTex;
        _itemButton.TextureHover = _itemButton.TextureNormal;

        // Start collapsed + rotated, animate open with clockwise sweep
        _node2d.Scale          = Vector2.Zero;
        _node2d.RotationDegrees = -45f;
        var tween = CreateTween().SetParallel(true);
        tween.TweenProperty(_node2d, "scale", Vector2.One, OpenDuration)
             .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_node2d, "rotation_degrees", 0f, OpenDuration)
             .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        float s = _node2d.Scale.X;
        if (s <= 0f) return;

        // Spokes and spoke-end circles disappear once a verb is committed
        if (!_confirming)
        {
            float   r    = _node2d.Rotation;
            float   cr   = SpokeCircleRadius * s;
            var     tips = new Vector2[_buttonCenters.Length];
            for (int i = 0; i < _buttonCenters.Length; i++)
            {
                tips[i] = _buttonCenters[i].Rotated(r) * s;
                DrawLine(Vector2.Zero, tips[i], Colors.Black, 3f, true);
            }
            foreach (var tip in tips)
            {
                DrawCircle(tip, cr, Colors.White);
                DrawArc(tip, cr, 0f, Mathf.Tau, 48, Colors.Black, 2.5f, true);
            }
        }

        // White hub circle with black outline
        DrawCircle(Vector2.Zero, HubRadius, Colors.White);
        DrawArc(Vector2.Zero, HubRadius, 0f, Mathf.Tau, 48, Colors.Black, 2.5f, true);
    }

    // On mobile, Godot emits both InputEventScreenTouch and an emulated InputEventMouseButton
    // for the same physical gesture. We only act on the "real" release for the current device.
    private bool IsRealRelease(InputEvent @event) =>
        parentScene.isTouch
            ? @event is InputEventScreenTouch  st && !st.Pressed
            : @event is InputEventMouseButton  mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left;

    private bool IsAnyRelease(InputEvent @event) =>
        (@event is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left) ||
        (@event is InputEventScreenTouch  st && !st.Pressed);

    public override void _Input(InputEvent @event)
    {
        if (_closing)
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_confirming)
        {
            // Swallow ALL release types; only the real one triggers an interrupt.
            if (IsAnyRelease(@event))
            {
                GetViewport().SetInputAsHandled();
                if (IsRealRelease(@event))
                {
                    if (OnCommit != null)
                    {
                        StartClose();
                    }
                    else
                    {
                        Vector2 screenPos = @event is InputEventScreenTouch t2
                            ? t2.Position
                            : GetViewport().GetMousePosition();
                        Vector2 localPos = parentScene.ToLocal(
                            GetViewport().GetCanvasTransform().AffineInverse() * screenPos);
                        parentScene.InterruptVerbCoin(localPos);
                    }
                }
            }
            else
            {
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (@event is InputEventMouseMotion)
            UpdateModeFromPos(GetGlobalMousePosition());
        else if (@event is InputEventScreenDrag drag)
            UpdateModeFromPos(GetViewport().GetCanvasTransform().AffineInverse() * drag.Position);
        else if (IsAnyRelease(@event))
        {
            GetViewport().SetInputAsHandled();
            if (_requirePressBeforeCommit) { _requirePressBeforeCommit = false; return; }
            if (IsRealRelease(@event)) CommitSelection();
        }
    }

    private void CommitSelection()
    {
        // No verb selected — close immediately with no confirmation
        if (parentScene.mainScene.GetInteractMode() == Globals.InteractModes.walk)
        {
            StartClose();
            return;
        }

        _confirming = true;

        if (OnCommit != null)
            OnCommit(parentScene.mainScene.GetInteractMode());
        else
            parentScene.TriggerInteractAtPressPos();

        // Hide buttons and show selected mode icon at hub
        SetHighlight(-1);
        foreach (var b in _buttons) b.Visible = false;

        Texture2D icon = parentScene.mainScene.GetInteractMode() switch
        {
            Globals.InteractModes.look      => _lookButton.TextureNormal,
            Globals.InteractModes.talk      => _talkButton.TextureNormal,
            Globals.InteractModes.use       => _useButton.TextureNormal,
            Globals.InteractModes.item      => _itemButton.TextureNormal,
            Globals.InteractModes.inventory => _inventoryButton.TextureNormal,
            _                               => null,
        };

        if (icon != null)
        {
            _hubIcon.Texture = icon;
            _hubIcon.Scale   = Vector2.One;
            _hubIcon.Visible = true;
        }

        GetTree().CreateTimer(ConfirmDelay, true)
                 .Connect("timeout", Callable.From(StartClose));
    }

    private void StartClose()
    {
        if (_closing) return;
        _closing = true;
        var tween = CreateTween().SetParallel(true);
        tween.TweenProperty(_node2d, "scale", Vector2.Zero, CloseDuration)
             .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.TweenProperty(_node2d, "rotation_degrees", -45f, CloseDuration)
             .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.Chain().TweenCallback(Callable.From(OnClose ?? parentScene.OnVerbCoinRelease));
    }

    private void ClampButtonsToInnerRect()
    {
        var     ct        = GetViewport().GetCanvasTransform();
        var     inner     = parentScene.mainScene.CelBorderInnerRect;
        float   sx        = ct.Scale.X;
        float   sy        = ct.Scale.Y;
        float   margin    = SpokeCircleRadius * sx;
        Vector2 hubScreen = ct * GlobalPosition;

        // Clamp bounds expressed in _node2d local space
        Vector2 localMin = new(
            (inner.Position.X + margin - hubScreen.X) / sx,
            (inner.Position.Y + margin - hubScreen.Y) / sy);
        Vector2 localMax = new(
            (inner.End.X - margin - hubScreen.X) / sx,
            (inner.End.Y - margin - hubScreen.Y) / sy);

        float minHub    = HubRadius + SpokeCircleRadius;
        float minCircle = SpokeCircleRadius * 2f;

        // Initial pass: clamp to border, push away from hub
        for (int i = 0; i < _buttons.Length; i++)
            _buttonCenters[i] = Constrain(_buttonCenters[i], localMin, localMax, minHub);

        // Iterative pairwise separation — early-exit when all clear
        for (int iter = 0; iter < 10; iter++)
        {
            bool anyOverlap = false;
            for (int i = 0; i < _buttons.Length; i++)
            {
                for (int j = i + 1; j < _buttons.Length; j++)
                {
                    Vector2 d    = _buttonCenters[j] - _buttonCenters[i];
                    float   dist = d.Length();
                    if (dist >= minCircle) continue;
                    anyOverlap = true;
                    Vector2 push = (dist > 0f ? d / dist : Vector2.Right) * ((minCircle - dist) * 0.5f);
                    _buttonCenters[i] = Constrain(_buttonCenters[i] - push, localMin, localMax, minHub);
                    _buttonCenters[j] = Constrain(_buttonCenters[j] + push, localMin, localMax, minHub);
                }
            }
            if (!anyOverlap) break;
        }

        // Write back to button nodes
        for (int i = 0; i < _buttons.Length; i++)
        {
            var     b = _buttons[i];
            Vector2 c = _buttonCenters[i];
            b.OffsetLeft   = c.X - _buttonHalfSizes[i].X;
            b.OffsetRight  = c.X + _buttonHalfSizes[i].X;
            b.OffsetTop    = c.Y - _buttonHalfSizes[i].Y;
            b.OffsetBottom = c.Y + _buttonHalfSizes[i].Y;
            b.PivotOffset  = _buttonHalfSizes[i];
        }
    }

    // Clamps p to [localMin, localMax] then ensures it's at least minHub from the hub (origin).
    private static Vector2 Constrain(Vector2 p, Vector2 localMin, Vector2 localMax, float minHub)
    {
        p.X = Mathf.Clamp(p.X, localMin.X, localMax.X);
        p.Y = Mathf.Clamp(p.Y, localMin.Y, localMax.Y);
        if (p.LengthSquared() < minHub * minHub)
            p = (p.LengthSquared() > 0f ? p.Normalized() : Vector2.Up) * minHub;
        return p;
    }

    private void SetHighlight(int idx)
    {
        if (idx == _highlightedIdx) return;
        if (_highlightedIdx >= 0)
            _buttons[_highlightedIdx].Scale = Vector2.One;
        if (idx >= 0)
            _buttons[idx].Scale = Vector2.One * HighlightScale;
        _highlightedIdx = idx;
    }

    public void UpdateItemButton(Texture2D tex)
    {
        _itemButton.TextureNormal = tex;
        _itemButton.TextureHover  = tex;
    }

    private void UpdateModeFromPos(Vector2 canvasPos)
    {
        if      (_lookButton.GetGlobalRect().HasPoint(canvasPos))      { SetHighlight(0); parentScene.mainScene.SetInteractMode(Globals.InteractModes.look); }
        else if (_talkButton.GetGlobalRect().HasPoint(canvasPos))      { SetHighlight(1); parentScene.mainScene.SetInteractMode(Globals.InteractModes.talk); }
        else if (_useButton.GetGlobalRect().HasPoint(canvasPos))       { SetHighlight(2); parentScene.mainScene.SetInteractMode(Globals.InteractModes.use); }
        else if (_itemButton.GetGlobalRect().HasPoint(canvasPos))      { SetHighlight(3); parentScene.mainScene.SetInteractMode(Globals.InteractModes.item); }
        else if (_inventoryButton.GetGlobalRect().HasPoint(canvasPos)) { SetHighlight(4); parentScene.mainScene.SetInteractMode(Globals.InteractModes.inventory); }
        else                                                           { SetHighlight(-1); parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk); }
    }
}
