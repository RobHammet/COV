// SceneZone — marks a polygon area that overrides the normal ScalerStick-driven
// behaviour for any thing whose Position falls inside it.
//
// FreezeScale:  when true, scale updates are suppressed while inside. Things
//               keep whatever scale they had on entry, unless FrozenScale is
//               also set, in which case they snap to that exact value.
// FrozenScale:  snap scale (implies FreezeScale). Ignored when FreezeScale is false.
// ZIndexBoost:  added to each thing's default ZIndex while inside the zone.
//               Use a positive value to push things in front of a foreground
//               sprite they would otherwise disappear behind (e.g. a staircase).

using Godot;

[Tool]
[GlobalClass]
public partial class SceneZone : CollisionPolygon2D
{
    [Export] public bool  FreezeScale  = false;
    [Export(PropertyHint.Range, "0,10,0.01")] public float FrozenScale = 0f;
    [Export] public int   ZIndexBoost  = 0;
}
