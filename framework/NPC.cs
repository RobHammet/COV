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
using System.Collections.Generic;

public partial class NPC : thing
{
    public enum Direction { up = 0, down = 1, left = 3, right = 4 }

    [Export] public Color     dialogColor      = Colors.White;
    [Export] public Direction Facing;
    [Export(PropertyHint.Range, "0,200,")] public float character_speed = 100.0f;

    [Signal] public delegate void DestinationReachedEventHandler();
    [Signal] public delegate void FacingChangedEventHandler();

    // topPoint — world-space position just above the NPC's head, used to
    // anchor speech bubbles. Uses GlobalPosition so scale is automatically
    // applied via Godot's transform hierarchy.
    public Vector2 topPoint
    {
        get
        {
            Vector2 tp = GetNode<Node2D>("TopPoint").GlobalPosition;
            bool    lr = Facing == Direction.left || Facing == Direction.right;
            if (lr)
                return new Vector2(
                    Facing == Direction.right ? 2f * GlobalPosition.X - tp.X : tp.X,
                    tp.Y);
            return new Vector2(GlobalPosition.X, tp.Y);
        }
    }

    public NavigationAgent2D navigationAgent2D;
    public bool              isWalking      = false;
    public bool              isFastWalking  = false;
    public float             character_fastwalk_speed;
    public int               anim_fps       = 8;

    // ---------------------------------------------------------------------------
    // _Ready
    // ---------------------------------------------------------------------------
    public override void _Ready()
    {
        base._Ready();

        // NPC shadows sit slightly higher than prop shadows.
        if (shadow != null)
            shadow.Offset = new Vector2(0, -(float)0.46 * sprite.Texture.GetHeight() / sprite.Vframes);

        character_fastwalk_speed = character_speed + character_speed / 100f * 85f;

        navigationAgent2D = GetNode<NavigationAgent2D>("NavigationAgent2D");

        StopWalking();
        isFastWalking = false;
        ChangeFacing(Facing);

        Connect("DestinationReached", new Callable(parentScene, "_on_Character_DestinationReached"));
        Connect("FacingChanged",      new Callable(parentScene, "_on_Character_FacingChanged"));
    }

    // ---------------------------------------------------------------------------
    // _Process — animation and movement tick.
    // ---------------------------------------------------------------------------
    public override void _Process(double delta)
    {
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
        if (Globals.showDebugTools)
            DrawCircle(ToLocal(topPoint), 5, Colors.DarkRed);
    }

    // ---------------------------------------------------------------------------
    // Navigation
    // ---------------------------------------------------------------------------

    public void GoToLocation(Vector2 pos)
    {
        StartWalking();
        navigationAgent2D.TargetPosition = pos;
    }

    public void GoToThing(thing target) => GoToLocation(target.interactPoint);

    public void MoveAlongPath(double delta)
    {
        Vector2 target = navigationAgent2D.GetNextPathPosition();

        if (Position.DistanceTo(target) <= navigationAgent2D.TargetDesiredDistance)
        {
            navigationAgent2D.TargetPosition = Position;
        }
        else
        {
            float   speed    = isFastWalking ? character_fastwalk_speed : character_speed;
            Vector2 velocity = Position.DirectionTo(target).Normalized() * speed * (float)delta * Scale.X;

            if (Math.Abs(velocity.Y) > Math.Abs(velocity.X))
                Facing = velocity.Y < 0 ? Direction.up : Direction.down;
            else
                Facing = velocity.X < 0 ? Direction.left : Direction.right;

            GlobalPosition += velocity;
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
}
