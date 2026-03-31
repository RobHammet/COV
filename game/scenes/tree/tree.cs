using Godot;
using System;

public partial class tree : scene_script
{
	// public Globals.SceneFlag[] flags = null;
	// Called when the node enters the scene tree for the first time.
	// public override void _Ready()
	// {
	// 	base._Ready();
		
	
	// 	// flags = new Globals.SceneFlag[2];
	// 	// flags[0] = new Globals.SceneFlag(((scene_script)this).displayName, "birdsGone", false);
	// }

	// // Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }
	public void _on_exit_to_housefront_body_entered(Node2D body) {
		mainScene.ChangeSceneToFile(Scenes.HOUSEFRONT, new Vector2(306,706), Character.Direction.down);
	}
}
