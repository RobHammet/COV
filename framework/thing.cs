// thing.cs — base class for all clickable scene objects (props, items, NPCs).
//
// Extend this class for any interactive object in a scene. Override
// SpecificLook/Use/Talk/UseItem to implement custom behaviour; the base
// TryLook/TryUse/TryTalk methods provide sensible defaults.
//
// SCALE SYSTEM
//   ScalerStick (Line2D with 3 points) defines two depth zones.
//   Each thing's Scale is updated in _Process() based on its Y position
//   unless isScaleFrozen is true (object inside a FreezeScaleRegion polygon).
//   originalScale offsets the scale so the object's starting size is preserved.

using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

[Tool]
[GlobalClass]
[Icon("res://game/icons/thing.svg")]
public partial class thing : Node2D
{
    [Export] public string                displayName;
    [Export] public Color                 dialogColor           = Colors.White;
    [Export] public bool                  castsShadow           = false;
    [Export] public Vector2               shadowOffset          = Vector2.Zero;
    [Export] public InventoryItem.ItemType inventoryItemType    = InventoryItem.ItemType.none;
    [Export] public bool                  lookClosely           = false;
    [Export] public string[]              lookText;
    [Export] public string                interactionFile;
    [Export] public bool                  startingScaleAsBaseline = true;
    [Export] public bool                  isScaleFrozen         = false;

    public Sprite2D           sprite;
    public bool               hasSprite    = false;
    public AnimationPlayer    animationPlayer;
    public bool               isAnimated   = false;
    public CollisionPolygon2D clickArea;
    public Vector2[]          clickPolygon;
    public scene_script       parentScene;
    public ScalerStick        scalerStick;
    public CollisionPolygon2D freezeScaleRegion;
    public float              originalScale;
    public float              max_for_normal1;
    public float              max_for_normal2;
    // ScalerStick reference Y values in scene (parent) space.
    // Cached once in _Ready so a positioned ScalerStick node works correctly.
    private float             _scalerY0, _scalerY1, _scalerY2;

    public Vector2 interactPoint { get; set; } = Vector2.Zero;
    public Vector2 centerPoint   { get; set; } = Vector2.Zero;
    public Vector2 basePoint     { get; set; } = Vector2.Zero;

    public virtual Vector2 topPoint
    {
        get
        {
            var node = GetNodeOrNull<Node2D>("TopPoint");
            if (node != null) return node.GlobalPosition;
            if (clickPolygon == null || clickPolygon.Length == 0)
                return centerPoint - new Vector2(0, 32f);
            float minY = float.MaxValue;
            foreach (var pt in clickPolygon)
                if (pt.Y < minY) minY = pt.Y;
            return new Vector2(centerPoint.X, minY);
        }
    }

    public virtual NPC.Direction DialogFacing
    {
        get
        {
            var egoNode = parentScene?.ego;
            if (egoNode == null) return NPC.Direction.down;
            float dx    = egoNode.Position.X - Position.X;
            float dy    = egoNode.Position.Y - Position.Y;
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (Math.Abs(angle) >= 60 && Math.Abs(angle) <= 120)
                return angle > 0 ? NPC.Direction.up : NPC.Direction.down;
            return Math.Abs(angle) < 90 ? NPC.Direction.left : NPC.Direction.right;
        }
    }

    public Sprite2D shadow;
    [Export] public bool isExist  = true;
    [Export] public bool isHidden = false;

    // Defaults captured at end of _Ready() — used by MainScene to detect state changes.
    public bool    _defaultIsExist;
    public bool    _defaultIsHidden;
    public Vector2 _defaultPosition;
    public int     _defaultFrame;
    public string  _defaultAnimation;
    public bool    _defaultAnimPlaying;

    // Game-specific fallback responses for unhandled interactions.
    // Set these from game code (e.g. a ModuleInitializer in GameData.cs)
    // before any interactions occur. Empty arrays suppress the fallback.
    public static string[] TalkFallbacks    = [];
    public static string[] UseFallbacks     = [];
    public static string[] UseItemFallbacks = [];

    private NPC ego;
    private Vector2    prevPos;
    private Vector2    prevScale;
    private JsonObject _interactionData;

    public override void _EnterTree()
    {
        base._EnterTree();
        prevPos   = Position;
        prevScale = Scale;
    }

    public override void _Draw()
    {
        base._Draw();
        if (!Globals.showDebugTools || !Globals.showDebugGraphics) return;
        if (clickPolygon != null && clickPolygon.Length > 0)
        {
            var pts = new Vector2[clickPolygon.Length + 1];
            for (int i = 0; i < clickPolygon.Length; i++)
                pts[i] = (clickPolygon[i] - Position) / Scale;
            pts[clickPolygon.Length] = (clickPolygon[0] - Position) / Scale;
            DrawPolyline(pts, Colors.Red, 2);
        }
        DrawCircle((basePoint     - Position) / Scale, 3, Colors.Blue);
        DrawCircle((centerPoint   - Position) / Scale, 5, Colors.Yellow);
        DrawCircle((interactPoint - Position) / Scale, 3, Colors.Cyan);
        DrawLine((centerPoint - Position) / Scale, (interactPoint - Position) / Scale, Colors.Cyan);
    }

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) { EditorSetup(); return; }

        parentScene = (Owner as scene_script) ?? (GetParent() as scene_script);
        ego   = parentScene?.ego;

        try { freezeScaleRegion = parentScene?.FindChild("FreezeScaleRegion") as CollisionPolygon2D; }
        catch { freezeScaleRegion = null; }

        scalerStick = parentScene?.FindChild("ScalerStick") as ScalerStick;
        if (scalerStick != null)
        {
            float oy    = scalerStick.Position.Y;
            _scalerY0   = oy + scalerStick.Points[0].Y;
            _scalerY1   = oy + scalerStick.Points[1].Y;
            _scalerY2   = oy + scalerStick.Points[2].Y;
            max_for_normal1 = _scalerY1 - _scalerY0;
            max_for_normal2 = _scalerY2 - _scalerY1;
        }

        if (startingScaleAsBaseline && scalerStick != null)
        {
            if (Position.Y <= _scalerY1)
                originalScale = 0.25f + 0.75f * (Position.Y - _scalerY0) / max_for_normal1;
            else
                originalScale = (Position.Y - _scalerY1) / max_for_normal2 + 1f;
            originalScale -= 1f;
        }
        else
        {
            originalScale = 0f;
        }

        // Find Sprite2D and AnimationPlayer children using is-pattern.
        foreach (var child in GetChildren())
        {
            if (child is Sprite2D s)
            {
                sprite    = s;
                hasSprite = true;
                foreach (var grandchild in s.GetChildren())
                    if (grandchild is AnimationPlayer ap)
                    {
                        animationPlayer = ap;
                        isAnimated      = true;
                    }
            }
        }

        SetPoints();

        // Apply initial visibility from exported state flags.
        bool visible = isExist && !isHidden;
        if (hasSprite) sprite.Visible = visible;

        // Check scene script first (things subsection), then fall back to:
        //   1. interactionFile export (explicit path, .covscript preferred over .json)
        //   2. Script co-located with the thing's own .tscn
        //   3. legacy game/data/things/{Name}.json
        _interactionData = parentScene?._sceneData?["things"]?[Name]?.AsObject();
        if (_interactionData == null)
        {
            string basePath;
            if (!string.IsNullOrEmpty(interactionFile))
                basePath = $"res://{interactionFile.Replace(".json", "").Replace(".covscript", "")}";
            else if (!string.IsNullOrEmpty(SceneFilePath))
                basePath = SceneFilePath.Replace(".tscn", "");
            else
                basePath = $"res://game/data/things/{Name}";

            string covPath  = basePath + ".covscript";
            string jsonPath = basePath + ".json";
            if (FileAccess.FileExists(covPath))
                _interactionData = CovScript.Parse(FileAccess.GetFileAsString(covPath));
            else if (FileAccess.FileExists(jsonPath))
                _interactionData = JsonNode.Parse(FileAccess.GetFileAsString(jsonPath))?.AsObject();
        }

        if (castsShadow && hasSprite)
        {
            shadow = new Sprite2D();
            shadow.Texture         = sprite.Texture;
            shadow.Offset          = sprite.Offset + shadowOffset;
            shadow.SelfModulate    = new Color(0, 0, 0, 1f);
            shadow.ZAsRelative     = true;
            shadow.ZIndex          = 0;
            shadow.Hframes         = sprite.Hframes;
            shadow.Vframes         = sprite.Vframes;
            shadow.Frame           = sprite.Frame;
            shadow.FlipH           = !sprite.FlipH;
            AddChild(shadow);
            MoveChild(shadow, 0);
        }

        _defaultIsExist     = isExist;
        _defaultIsHidden    = isHidden;
        _defaultPosition    = Position;
        _defaultFrame       = hasSprite   ? sprite.Frame                      : 0;
        _defaultAnimation   = isAnimated  ? animationPlayer.CurrentAnimation  : "";
        _defaultAnimPlaying = isAnimated  && animationPlayer.IsPlaying();
    }

    // SetPoints needs a few extra frames after _Ready to let scale/polygon settle.
    private int _settleFrames = 4;

    public void SetPoints()
    {
        clickArea = GetNode<CollisionPolygon2D>("CollisionPolygon2D");
        var newPoly = new Vector2[clickArea.Polygon.Length];
        for (int i = 0; i < clickArea.Polygon.Length; i++)
            newPoly[i] = (clickArea.Polygon[i] + clickArea.Position) * Scale + Position;
        clickPolygon = newPoly;

        float sumX = 0, sumY = 0, maxY = -100;
        foreach (Vector2 pt in clickPolygon)
        {
            sumX += pt.X; sumY += pt.Y;
            if (pt.Y > maxY) maxY = pt.Y;
        }
        centerPoint = new Vector2(sumX / clickPolygon.Length, sumY / clickPolygon.Length);
        basePoint   = new Vector2(centerPoint.X, maxY);

        var ipNode = GetNodeOrNull<Node2D>("InteractPoint");
        interactPoint = ipNode != null
            ? (hasSprite && sprite.FlipH)
                ? new Vector2(Position.X - ipNode.Position.X * Scale.X, Position.Y + ipNode.Position.Y * Scale.Y)
                : Position + ipNode.Position * Scale
            : Position;
    }

    public void OnMove()
    {
        SetPoints();
        prevPos = Position;
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint()) return;

        if (scalerStick != null)
        {
            if (freezeScaleRegion != null)
                isScaleFrozen = Geometry2D.IsPointInPolygon(Position, freezeScaleRegion.Polygon);
            if (!isScaleFrozen)
            {
                float s;
                if (Position.Y <= _scalerY1)
                    s = 0.25f + 0.75f * (Position.Y - _scalerY0) / max_for_normal1;
                else
                    s = (Position.Y - _scalerY1) / max_for_normal2 + 1f;
                s -= originalScale;
                if (!startingScaleAsBaseline)
                    s *= scalerStick.ScaleMultiplier;
                Scale = new Vector2(s, s);
            }
        }

        if (Position != prevPos || Scale != prevScale) { prevScale = Scale; OnMove(); }

        if (castsShadow && shadow != null && sprite != null)
        {
            shadow.Frame           = sprite.Frame;
            shadow.FlipH           = !sprite.FlipH;
            shadow.Scale           = sprite.Scale;
            shadow.Offset          = sprite.Offset + shadowOffset;
        }

        if (Globals.showDebugTools) QueueRedraw();
        if (_settleFrames > 0) { SetPoints(); _settleFrames--; }
    }

    public void InitInteract(Globals.InteractModes mode)
    {
        if (!isExist) return;
        switch (mode)
        {
            case Globals.InteractModes.walk: TryWalk(); break;
            case Globals.InteractModes.look: TryLook(); break;
            case Globals.InteractModes.talk: TryTalk(); break;
            case Globals.InteractModes.use:  TryUse();  break;
            case Globals.InteractModes.item: TryUseItem(parentScene.mainScene.usingItem); break;
        }
    }

    private int lastLookTextRead = -1;

    public void TryLook()
    {
        if (parentScene.eventQueue.isRunning) return;
        ego?.StopWalking();
        if (ego != null)
        {
            if (lookClosely)
                parentScene.eventQueue.AddEventMove(ego, interactPoint, _interruptable: true);
            parentScene.eventQueue.AddEventChangeFacingToLookAt(ego, this, _interruptable: true);
        }
        bool handled = SpecificLook(ego, parentScene.eventQueue);
        if (!handled)
        {
            int  t           = lastLookTextRead + 1;
            bool hasLookText = lookText != null && lookText.Length > 0;
            if (hasLookText && t >= lookText.Length) t = 0;
            string text = hasLookText ? lookText[t] : $"IT'S A {(displayName ?? Name).ToUpper()}.";
            if (hasLookText) lastLookTextRead = t;
            if (ego != null)
                parentScene.eventQueue.AddEventThink(ego, text);
            else
                parentScene.eventQueue.AddEventNarrate(text);
        }
    }

    public void TryUse()
    {
        if (ego != null)
        {
            parentScene.eventQueue.AddEventMove(ego, interactPoint, _interruptable: true);
            parentScene.eventQueue.AddEventChangeFacingToLookAt(ego, this, _interruptable: true);
        }
        bool handled = SpecificUse(ego, parentScene.eventQueue);
        if (!handled && UseFallbacks.Length > 0)
        {
            string fb = UseFallbacks[(int)(GD.Randf() * UseFallbacks.Length)];
            if (ego != null) parentScene.eventQueue.AddEventThink(ego, fb);
            else             parentScene.eventQueue.AddEventNarrate(fb);
        }
    }

    public void TryTalk()
    {
        if (ego != null)
        {
            parentScene.eventQueue.AddEventMove(ego, interactPoint, _interruptable: true);
            parentScene.eventQueue.AddEventChangeFacingToLookAt(ego, this, _interruptable: true);
        }
        bool handled = SpecificTalk(ego, parentScene.eventQueue);
        if (!handled && TalkFallbacks.Length > 0)
        {
            string fb = TalkFallbacks[(int)(GD.Randf() * TalkFallbacks.Length)];
            if (ego != null) parentScene.eventQueue.AddEventSpeak(ego, fb, Vector2.Zero);
            else             parentScene.eventQueue.AddEventNarrate(fb);
        }
    }

    public void TryUseItem(InventoryItem.ItemType item)
    {
        if (item == InventoryItem.ItemType.none) return;
        if (ego != null)
        {
            parentScene.eventQueue.AddEventMove(ego, interactPoint, _interruptable: true);
            parentScene.eventQueue.AddEventChangeFacingToLookAt(ego, this, _interruptable: true);
        }
        bool handled = SpecificUseItem(item);
        if (!handled && UseItemFallbacks.Length > 0)
        {
            string fb = UseItemFallbacks[(int)(GD.Randf() * UseItemFallbacks.Length)];
            if (ego != null) parentScene.eventQueue.AddEventSpeak(ego, fb, Vector2.Zero);
            else             parentScene.eventQueue.AddEventNarrate(fb);
        }
    }

    public void TryWalk()
    {
        if (_interactionData?.ContainsKey("walk") != true) return;
        parentScene.eventQueue = new EventSequence(parentScene);
        scene_script.PopulateEventQueue(parentScene.eventQueue, _interactionData["walk"].AsArray(), parentScene, this);
    }

    public virtual bool SpecificLook(NPC ego, EventSequence eventSequence)
    {
        if (_interactionData?.ContainsKey("look") != true) return false;
        scene_script.PopulateEventQueue(eventSequence, _interactionData["look"].AsArray(), parentScene, this);
        return true;
    }

    public virtual bool SpecificUse(NPC ego, EventSequence eventSequence)
    {
        if (_interactionData?.ContainsKey("use") != true) return false;
        scene_script.PopulateEventQueue(eventSequence, _interactionData["use"].AsArray(), parentScene, this);
        return true;
    }

    public virtual bool SpecificTalk(NPC ego, EventSequence eventSequence)
    {
        if (_interactionData?.ContainsKey("talk") != true) return false;
        scene_script.PopulateEventQueue(eventSequence, _interactionData["talk"].AsArray(), parentScene, this);
        return true;
    }

    public virtual bool SpecificUseItem(InventoryItem.ItemType item)
    {
        var useItem = _interactionData?["use_item"]?.AsObject();
        if (useItem?.ContainsKey(item.ToString()) != true) return false;
        scene_script.PopulateEventQueue(parentScene.eventQueue, useItem[item.ToString()].AsArray(), parentScene, this);
        return true;
    }

    public bool AddToInventory()
    {
        if (inventoryItemType == InventoryItem.ItemType.none) return false;
        parentScene.mainScene.inventory.Add(new InventoryItem(inventoryItemType));
        ToggleExist(false);
        return true;
    }

    private void ApplyVisibility()
    {
        bool visible = isExist && !isHidden;
        if (hasSprite) sprite.Visible = visible;
        if (castsShadow && shadow != null) shadow.Visible = visible;
    }

    // ToggleExist — controls whether the thing participates in the scene at all.
    // false: not interactive, not visible. true: interactive, respects isHidden.
    public void ToggleExist(bool? value = null)
    {
        isExist = value ?? !isExist;
        ApplyVisibility();
    }

    // ToggleHide — controls visibility only; thing remains interactive when hidden.
    public void ToggleHide(bool? value = null)
    {
        isHidden = value ?? !isHidden;
        ApplyVisibility();
    }

    // ── Editor ──────────────────────────────────────────────────────────────────
    // Auto-spawns required child nodes when the node is first added to a scene in
    // the editor. Override in subclasses (call base) to add subclass-specific children.

    protected virtual void EditorSetup()
    {
        EnsureChild<CollisionPolygon2D>("CollisionPolygon2D");
        EnsureChild<Node2D>("InteractPoint");
        EnsureChild<Sprite2D>("Sprite2D", s =>
            s.Material = ResourceLoader.Load<ShaderMaterial>("res://shaders/materials/light_and_shadows.tres"));
    }

    protected T EnsureChild<T>(string name, Action<T> configure = null) where T : Node, new()
    {
        if (GetNodeOrNull<T>(name) is T existing) return existing;
        var child = new T { Name = name };
        configure?.Invoke(child);
        AddChild(child);
        child.Owner = GetTree().EditedSceneRoot;
        return child;
    }
}
