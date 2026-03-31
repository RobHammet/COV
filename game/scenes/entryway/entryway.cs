using Godot;
using System;
using System.Collections;
using System.Collections.Generic;

public partial class entryway : scene_script
{
	//[Export] Godot.Collections.Array<Resource> customResources; //GDScript
	public void _on_into_the_house_body_entered (Node body) {
	
		
		this.SuspendSceneInput();
		EventSequence startSequence = new EventSequence(this);
		Character character = this.GetNode<Character>("Character");
		startSequence.AddEventChangeFacing(character, Character.Direction.right);
		startSequence.AddEventMove(character, new Vector2(118, 415));
        mainScene.ChangeSceneToFile(Scenes.SITTINGROOM, new Vector2(-22, 433), Character.Direction.right, startSequence);      




	}
}
