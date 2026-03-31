using Godot;
using System;

public partial class shadertest : thing
{
	// Called when the node enters the scene tree for the first time.
	// public override void _Ready()
	// {
	// }

	// // Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }

	public override bool SpecificLook(Character character, EventSequence eventSequence) {


		if ((bool)parentScene.GetFlag("ballLooked").Value) {
			eventSequence.AddEventSpeak(character, "I DON'T WANT TO GO LOOK AT THAT AGAIN.", new Vector2(0,0), true);
			eventSequence.AddEventAddFlag(new Globals.SceneFlag(parentScene.Name, "ballLooked", false));
		} else {
			eventSequence.AddEventMove(character, this.interactPoint, true);
			eventSequence.AddEventChangeFacingToLookAt(character, this, true);
			eventSequence.AddEventSpeak(character, "IT'S A DAMN BALL.", new Vector2(0,0), true);
			// eventSequence.AddEventAddToInventory(this);

			eventSequence.AddEventAddFlag(new Globals.SceneFlag(parentScene.Name, "ballLooked", true));
		}

        return true;
    }
}
