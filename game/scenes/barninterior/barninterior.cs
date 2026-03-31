using Godot;
using System;

public partial class barninterior : scene_script
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		base._Ready();
		  if ((bool)GetFlag("axeTaken").Value == true) {
            this.GetNode<thing>("axe").Disappear();
        }
	}

	// // Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }

	public void _on_exit_to_barnfront_body_entered(Node2D body) {
		mainScene.ChangeSceneToFile(Scenes.BARNFRONT, new Vector2(350,415), Character.Direction.down);
	}
}
