using Godot;
using System;

public partial class upstairs : scene_script
{
	// // Called when the node enters the scene tree for the first time.
	// public override void _Ready()
	// {
	// }

	// // Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }

	public void _on_exit_to_sittingroom_body_entered(Node body) {
        EventSequence walkdownstepsSequence = new EventSequence(this);
		Character character = this.GetNode<Character>("Character");
        walkdownstepsSequence.AddEventMove(character, new Vector2(150,360), true);
		mainScene.ChangeSceneToFile(Scenes.SITTINGROOM,new Vector2(128,128),Character.Direction.down, walkdownstepsSequence);
	}
}
