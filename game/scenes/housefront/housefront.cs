using Godot;
using System;

public partial class housefront : scene_script
{
	// private Camera2D camera;
	// Declare member variables here. Examples:
	// private int a = 2;
	// private string b = "text";

	// Called when the node enters the scene tree for the first time.
	// public override void _Ready()
	// {
	// 	// camera = this.GetNode<Camera2D>("Camera2D");
	// 	// camera.MakeCurrent();
		
	// }

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// 	// GD.Print("iscurrent " + camera.IsCurrent().ToString())	;
	// 	// camera.MakeCurrent();
	// }
	public void _on_exit_to_porch_body_entered (Node body) {
			mainScene.ChangeSceneToFile(Scenes.THEPORCH);        
	}
	public void _on_exit_to_barn_body_entered (Node body) {
			mainScene.ChangeSceneToFile(Scenes.BARNFRONT);        
	}

	public void _on_exit_to_tree_body_entered (Node body) {
			mainScene.ChangeSceneToFile(Scenes.TREE);        
	}


    // public override void _Input (InputEvent @event) {
	// 	if (@event is InputEventKey eventKey) {

	// 		if (eventKey.Pressed)
	// 			if (eventKey.Keycode is Key.C) {
	// 				GD.Print("C");
	// 				camera.Position += new Vector2(10f, 0f);
	// 			} 
	// 	}
	// }
            
}
