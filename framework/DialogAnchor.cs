// DialogAnchor.cs — positional anchor for speech/thought bubbles in insert scenes.
//
// Place one or more of these Node2Ds in an insert scene wherever a bubble tail
// should originate. Reference them by node name in JSON "actor" fields just as
// you would reference an NPC in a room scene.
//
//   { "action": "speak", "actor": "JackAnchor", "text": "HELLO." }
//   { "action": "think", "actor": "JackAnchor", "text": "HMMMM." }
//
// The node's GlobalPosition is used as the tail origin.
// Facing controls which side of the bubble the tail appears on.

using Godot;

public partial class DialogAnchor : Node2D
{
    [Export] public Color         dialogColor = Colors.White;
    [Export] public NPC.Direction Facing      = NPC.Direction.down;
}
