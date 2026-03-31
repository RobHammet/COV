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

public partial class thing : Node2D
{
    [Export] public string                displayName;
    [Export] public bool                  castsShadow           = false;
    [Export] public InventoryItem.ItemType inventoryItemType    = InventoryItem.ItemType.none;
    [Export] public bool                  lookClosely           = false;
    [Export] public string[]              lookText;
    [Export] public bool                  startingScaleAsBaseline = true;
    [Export] public bool                  isScaleFrozen         = false;

    public Sprite2D           sprite;
    public bool               hasSprite    = false;
    public AnimationPlayer    animationPlayer;
    public bool               isAnimated   = false;
    public CollisionPolygon2D clickArea;
    public Vector2[]          clickPolygon;
    public scene_script       parentScene;
    public Line2D             scalerStick;
    public CollisionPolygon2D freezeScaleRegion;
    public float              originalScale;
    public float              max_for_normal1;
    public float              max_for_normal2;

    public Vector2 interactPoint { get; set; } = Vector2.Zero;
    public Vector2 centerPoint   { get; set; } = Vector2.Zero;
    public Vector2 basePoint     { get; set; } = Vector2.Zero;

    public Sprite2D shadow;
    public bool     isExist = true;

    // Game-specific fallback responses for unhandled interactions.
    // Set these from game code (e.g. a ModuleInitializer in GameData.cs)
    // before any interactions occur. Empty arrays suppress the fallback.
    public static string[] TalkFallbacks    = [];
    public static string[] UseFallbacks     = [];
    public static string[] UseItemFallbacks = [];

    private Character character;
    private Vector2   prevPos;

    public override void _EnterTree()
    {
        base._EnterTree();
        prevPos = Position;
    }

    public override void _Draw()
    {
        base._Draw();
        if (!Globals.showDebugTools) return;
        if (clickPolygon != null && clickPolygon.Length > 0)
        {
            var pts = new Vector2[clickPolygon.Length + 1];
            for (int i = 0; i < clickPolygon.Length; i++)
                pts[i] = clickPolygon[i] - Position;
            pts[clickPolygon.Length] = clickPolygon[0] - Position;
            DrawPolyline(pts, Colors.Red, 2);
        }
        DrawCircle(basePoint    - Position, 3, Colors.Blue);
        DrawCircle(centerPoint  - Position, 5, Colors.Yellow);
        DrawCircle(interactPoint - Position, 3, Colors.Cyan);
        DrawLine(centerPoint - Position, interactPoint - Position, Colors.Cyan);
    }

    public override void _Ready()
    {
        parentScene = Owner as scene_script;
        character   = parentScene?.character;

        try { freezeScaleRegion = parentScene?.FindChild("FreezeScaleRegion") as CollisionPolygon2D; }
        catch { freezeScaleRegion = null; }

        scalerStick = parentScene?.FindChild("ScalerStick") as Line2D;
        if (scalerStick != null)
        {
            max_for_normal1 = scalerStick.Points[1].Y - scalerStick.Points[0].Y;
            max_for_normal2 = scalerStick.Points[2].Y - scalerStick.Points[1].Y;
        }

        if (startingScaleAsBaseline && scalerStick != null)
        {
            if (Position.Y <= scalerStick.Points[1].Y)
                originalScale = (Position.Y - scalerStick.Points[0].Y) / max_for_normal1;
            else
                originalScale = (Position.Y - scalerStick.Points[1].Y) / max_for_normal2 + 1f;
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

        if (castsShadow && hasSprite)
        {
            shadow = new Sprite2D();
            shadow.Texture      = sprite.Texture;
            shadow.Offset       = new Vector2(0, -(float)0.46 * sprite.Texture.GetHeight() / sprite.Vframes);
            shadow.SelfModulate = new Color(0, 0, 0, 1);
            shadow.Rotation     = 360;
            shadow.ZAsRelative  = true;
            shadow.ZIndex       = -1;
            shadow.Hframes      = sprite.Hframes;
            shadow.Vframes      = sprite.Vframes;
            shadow.Frame        = sprite.Frame;
            shadow.FlipH        = !sprite.FlipH;
            AddChild(shadow);
        }
    }

    int hasRunSetPoints = 0;

    public void SetPoints()
    {
        clickArea = GetNode<CollisionPolygon2D>("CollisionPolygon2D");
        var newPoly = new Vector2[clickArea.Polygon.Length];
        for (int i = 0; i < clickArea.Polygon.Length; i++)
            newPoly[i] = clickArea.Polygon[i] + Position;
        clickPolygon = newPoly;

        float sumX = 0, sumY = 0, maxY = -100;
        foreach (Vector2 pt in clickPolygon)
        {
            sumX += pt.X; sumY += pt.Y;
            if (pt.Y > maxY) maxY = pt.Y;
        }
        centerPoint = new Vector2(sumX / clickPolygon.Length, sumY / clickPolygon.Length);
        basePoint   = new Vector2(centerPoint.X, maxY);

        Vector2 ip = Position;
        try
        {
            var ipNode = GetNode<Node2D>("InteractPoint");
            if (ipNode != null)
                ip = (hasSprite && sprite.FlipH)
                    ? new Vector2(Position.X - ipNode.Position.X, Position.Y + ipNode.Position.Y)
                    : ipNode.Position + Position;
        }
        catch { }
        interactPoint = ip;
        hasRunSetPoints++;
    }

    public void OnMove()
    {
        SetPoints();
        prevPos = Position;
    }

    public override void _Process(double delta)
    {
        if (scalerStick != null)
        {
            if (freezeScaleRegion != null)
                isScaleFrozen = Geometry2D.IsPointInPolygon(Position, freezeScaleRegion.Polygon);
            if (!isScaleFrozen)
            {
                float s;
                if (Position.Y <= scalerStick.Points[1].Y)
                    s = (Position.Y - scalerStick.Points[0].Y) / max_for_normal1;
                else
                    s = (Position.Y - scalerStick.Points[1].Y) / max_for_normal2 + 1f;
                s -= originalScale;
                Scale = new Vector2(s, s);
            }
        }

        if (Position != prevPos) OnMove();

        if (castsShadow && shadow != null && sprite != null)
        {
            shadow.Frame = sprite.Frame;
            shadow.FlipH = !sprite.FlipH;
            shadow.Scale = sprite.Scale;
        }

        if (Globals.showDebugTools) QueueRedraw();
        if (hasRunSetPoints < 5) SetPoints();
    }

    public void InitInteract(Globals.InteractModes mode)
    {
        if (!isExist) return;
        switch (mode)
        {
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
        character.StopWalking();
        if (lookClosely)
        {
            parentScene.eventQueue.AddEventMove(character, interactPoint, _interruptable: true);
            parentScene.eventQueue.AddEventChangeFacingToLookAt(character, this, _interruptable: true);
        }
        else
        {
            parentScene.eventQueue.AddEventChangeFacingToLookAt(character, this, _interruptable: true);
        }
        bool handled = SpecificLook(character, parentScene.eventQueue);
        if (!handled)
        {
            int  t           = lastLookTextRead + 1;
            bool hasLookText = lookText != null && lookText.Length > 0;
            if (hasLookText && t >= lookText.Length) t = 0;
            string text = hasLookText ? lookText[t] : $"IT'S A {displayName.ToUpper()}.";
            if (hasLookText) lastLookTextRead = t;
            parentScene.eventQueue.AddEventThink(character, text);
        }
    }

    public void TryUse()
    {
        parentScene.eventQueue.AddEventMove(character, interactPoint, _interruptable: true);
        parentScene.eventQueue.AddEventChangeFacingToLookAt(character, this, _interruptable: true);
        bool handled = SpecificUse(character, parentScene.eventQueue);
        if (!handled && UseFallbacks.Length > 0)
        {
            var rng = new RandomNumberGenerator(); rng.Randomize();
            parentScene.eventQueue.AddEventThink(character, UseFallbacks[(int)(rng.Randf() * UseFallbacks.Length)]);
        }
    }

    public void TryTalk()
    {
        parentScene.eventQueue.AddEventMove(character, interactPoint, _interruptable: true);
        parentScene.eventQueue.AddEventChangeFacingToLookAt(character, this, _interruptable: true);
        bool handled = SpecificTalk(character, parentScene.eventQueue);
        if (!handled && TalkFallbacks.Length > 0)
        {
            var rng = new RandomNumberGenerator(); rng.Randomize();
            parentScene.eventQueue.AddEventSpeak(character, TalkFallbacks[(int)(rng.Randf() * TalkFallbacks.Length)], Vector2.Zero);
        }
    }

    public void TryUseItem(InventoryItem.ItemType item)
    {
        if (item == InventoryItem.ItemType.none) return;
        parentScene.eventQueue.AddEventMove(character, interactPoint, _interruptable: true);
        parentScene.eventQueue.AddEventChangeFacingToLookAt(character, this, _interruptable: true);
        bool handled = SpecificUseItem(item);
        if (!handled && UseItemFallbacks.Length > 0)
        {
            var rng = new RandomNumberGenerator(); rng.Randomize();
            parentScene.eventQueue.AddEventSpeak(character, UseItemFallbacks[(int)(rng.Randf() * UseItemFallbacks.Length)], Vector2.Zero);
        }
    }

    public virtual bool SpecificLook(Character character, EventSequence eventSequence)  => false;
    public virtual bool SpecificUse(Character character, EventSequence eventSequence)   => false;
    public virtual bool SpecificTalk(Character character, EventSequence eventSequence)  => false;
    public virtual bool SpecificUseItem(InventoryItem.ItemType item)                    => false;

    public bool AddToInventory()
    {
        if (inventoryItemType == InventoryItem.ItemType.none) return false;
        parentScene.mainScene.inventory.Add(new InventoryItem(inventoryItemType));
        Disappear();
        return true;
    }

    public void Disappear()
    {
        isExist = false;
        if (hasSprite) sprite.Visible = false;
        if (castsShadow && shadow != null) shadow.Visible = false;
    }
}
