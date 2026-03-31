using Godot;
using System;

public partial class barnfront : scene_script
{
	// Called when the node enters the scene tree for the first time.
	// public override void _Ready()
	// {
	// }

	// // Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }

	public void _on_exit_to_housefront_body_entered(Node2D body)  {

        mainScene.ChangeSceneToFile(Scenes.HOUSEFRONT, new Vector2(947,671), Character.Direction.left);


	}


	public void _on_exit_to_barninterior_body_entered(Node2D body)  {

        mainScene.ChangeSceneToFile(Scenes.BARNINTERIOR, new Vector2(340,295), Character.Direction.down);


	}
}
