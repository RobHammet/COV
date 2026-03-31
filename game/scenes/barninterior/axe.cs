using Godot;
using System;

public partial class axe : thing
{
	// Called when the node enters the scene tree for the first time.
	// public override void _Ready()
	// {
	// }

	// // Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }

	public override bool SpecificUse(Character character, EventSequence eventSequence) {

        eventSequence.AddEventMove(character, this.interactPoint, true);
        eventSequence.AddEventThink(character, "THIS MAY BE USEFUL.", null, true);

        // eventSequence.AddEventPlayAnimation(this.GetNode<Sprite2D>("Sprite2D").GetNode<AnimationPlayer>("AnimationPlayer"), "burn");


        eventSequence.AddEventAddToInventory(this);
        eventSequence.AddEventAddFlag(new Globals.SceneFlag(parentScene.Name, "axeTaken", true));

        return true;
    }

}
