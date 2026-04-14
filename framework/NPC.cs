// NPC.cs — base class for all scene actors (characters and NPCs).
//
// Extends thing with: direction facing, navigation via NavigationAgent2D,
// dialog colour, and fast-walk support. Character.cs extends this further
// with player-specific input handling.
//
// MOVEMENT
//   Call GoToLocation(pos) to start movement. MoveAlongPath() is called each
//   _Process() frame while isWalking is true. EmitSignal DestinationReached
//   when NavigationAgent2D reports IsNavigationFinished().

using Godot;
using System;

[Tool]
[GlobalClass]
[Icon("res://game/icons/npc.svg")]
public partial class NPC : thing
{
    public enum Direction { up = 0, down = 1, left = 3, right = 4 }

    [Export] public Color     dialogColor = Colors.White;
    [Export] public Direction Facing;
    // Snapshot of the exported Facing value taken at end of _Ready.
    // Used by MainScene.CaptureThingState for change detection on scene exit.
    public Direction _defaultFacing;
    [Export(PropertyHint.Range, "0,200,")] public float speed = 100.0f;

    [Signal] public delegate void DestinationReachedEventHandler();
    [Signal] public delegate void FacingChangedEventHandler();

    // topPoint — world-space position just above the NPC's head, used to
    // anchor speech bubbles. Uses GlobalPosition so scale is automatically
    // applied via Godot's transform hierarchy.
    public Vector2 topPoint
    {
        get
        {
            Vector2 tp = _topPointNode.GlobalPosition;
            bool    lr = Facing == Direction.left || Facing == Direction.right;
            if (lr)
                return new Vector2(
                    Facing == Direction.right ? 2f * GlobalPosition.X - tp.X : tp.X,
                    tp.Y);
            return new Vector2(GlobalPosition.X, tp.Y);
        }
    }

    public NavigationAgent2D navigationAgent2D;
    public CharacterBody2D   charBody;
    public bool              isWalking     = false;
    public bool              isFastWalking = false;
    public float             fastwalk_speed;

    private Node2D _topPointNode;

    // ---------------------------------------------------------------------------
    // _Ready
    // ---------------------------------------------------------------------------
    public override void _Ready()
    {
        if (Engine.IsEditorHint()) { EditorSetup(); return; }

        base._Ready();

        // NPC shadows sit slightly higher than prop shadows.
        if (shadow != null)
            shadow.Offset = new Vector2(0, -(float)0.46 * sprite.Texture.GetHeight() / sprite.Vframes);

        fastwalk_speed = speed * 1.85f;

        _topPointNode     = GetNode<Node2D>("TopPoint");
        navigationAgent2D = GetNode<NavigationAgent2D>("NavigationAgent2D");
        charBody          = GetNodeOrNull<CharacterBody2D>("charBody");

        StopWalking();
        isFastWalking = false;
        ChangeFacing(Facing);

        Connect("DestinationReached", new Callable(parentScene, "_on_Ego_DestinationReached"));
        Connect("FacingChanged",      new Callable(parentScene, "_on_Ego_FacingChanged"));
        _defaultFacing = Facing;
    }

    // ---------------------------------------------------------------------------
    // _Process — animation and movement tick.
    // ---------------------------------------------------------------------------
    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint()) return;

        base._Process(delta);

        if (isWalking)
        {
            switch (Facing)
            {
                case Direction.up:    animationPlayer.CurrentAnimation = "walkup";   break;
                case Direction.down:  animationPlayer.CurrentAnimation = "walkdown";  break;
                case Direction.left:  animationPlayer.CurrentAnimation = "walk"; sprite.FlipH = false; break;
                case Direction.right: animationPlayer.CurrentAnimation = "walk"; sprite.FlipH = true;  break;
            }
            MoveAlongPath(delta);
        }
    }

    // ---------------------------------------------------------------------------
    // _Draw — debug only.
    // ---------------------------------------------------------------------------
    public override void _Draw()
    {
        base._Draw();
        if (Globals.showDebugTools && Globals.showDebugGraphics)
            DrawCircle(ToLocal(topPoint), 5, Colors.DarkRed);
    }

    // ---------------------------------------------------------------------------
    // Navigation
    // ---------------------------------------------------------------------------

    public void GoToLocation(Vector2 pos)
    {
        StartWalking();
        Vector2 globalTarget = parentScene.ToGlobal(pos);
        navigationAgent2D.TargetPosition = globalTarget;
    }

    public void GoToThing(thing target) => GoToLocation(target.interactPoint);

    public void MoveAlongPath(double delta)
    {
        Vector2 target = navigationAgent2D.GetNextPathPosition();

        if (GlobalPosition.DistanceTo(target) <= navigationAgent2D.TargetDesiredDistance)
        {
            navigationAgent2D.TargetPosition = GlobalPosition;
        }
        else
        {
            float   moveSpeed = isFastWalking ? fastwalk_speed : speed;
            Vector2 velocity  = GlobalPosition.DirectionTo(target).Normalized() * moveSpeed * (float)delta * Scale.X;

            if (Math.Abs(velocity.Y) > Math.Abs(velocity.X))
                Facing = velocity.Y < 0 ? Direction.up : Direction.down;
            else
                Facing = velocity.X < 0 ? Direction.left : Direction.right;

            Position += velocity;
        }

        if (navigationAgent2D.IsNavigationFinished())
        {
            StopWalking();
            PathEndReached();
        }
    }

    public void PathEndReached()
    {
        EmitSignal(SignalName.DestinationReached);
    }

    // ---------------------------------------------------------------------------
    // Locomotion control
    // ---------------------------------------------------------------------------

    public void StartWalking()     => isWalking    = true;
    public void StopFastWalking()  => isFastWalking = false;
    public void StartFastWalking() => isFastWalking = true;

    public void StopWalking()
    {
        animationPlayer?.Stop();
        isWalking     = false;
        isFastWalking = false;
    }

    // ---------------------------------------------------------------------------
    // Facing
    // ---------------------------------------------------------------------------

    public void ChangeFacing(Direction newFacing)
    {
        Facing = newFacing;
        if (hasSprite)
        {
            switch (newFacing)
            {
                case Direction.left:  sprite.FlipH = false; break;
                case Direction.right: sprite.FlipH = true;  break;
            }
        }
        EmitSignal(SignalName.FacingChanged);
    }

    public void ChangeFacingToLookAt(thing target)
    {
        Vector2 tPos  = target.basePoint;
        float   xDiff = Position.X - tPos.X;
        float   yDiff = Position.Y - tPos.Y;
        double  angle = Math.Atan2(yDiff, xDiff) * 180.0 / Math.PI;

        const float low = 60f, high = 120f;
        if (Math.Abs(angle) >= low && Math.Abs(angle) <= high)
            ChangeFacing(angle > 0 ? Direction.up : Direction.down);
        else
            ChangeFacing(Math.Abs(angle) < 90 ? Direction.left : Direction.right);
    }

    // ── Editor ──────────────────────────────────────────────────────────────────

    protected override void EditorSetup()
    {
        base.EditorSetup(); // CollisionPolygon2D, InteractPoint
        EnsureChild<NavigationAgent2D>("NavigationAgent2D");
        EnsureChild<Node2D>("TopPoint");
    }
}
