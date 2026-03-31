using Godot;
using System;

public partial class title_splash : Node2D
{
	// private MainScene mainScene;

	// // Called when the node enters the scene tree for the first time.
	// public override void _Ready()
	// {
	// 	mainScene = (MainScene)this.GetParent().GetParent().GetParent();

	// 	mainScene.currentScene.SuspendSceneInput();		
	// 	mainScene.currentScene.Pause();
	// }

	// // Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }
    // public override void _Input (InputEvent @event) {

    //     if (@event is InputEventMouseButton eventMouseButton) {
            

    //         if (eventMouseButton.ButtonIndex is MouseButton.Left ) {
                

    //             if (eventMouseButton.Pressed) {

	// 				// GetViewport().SetInputAsHandled();

	// 				mainScene.currentScene.character.StopWalking();
	// 				mainScene.currentScene.character.isWalking = false;
	// 				mainScene.currentScene.isMouseLeftButtonDown = false;
	// 				mainScene.currentScene.isMouseLeftButtonClicked = false;
					
	// 				WaitAndShow();


	// 			}
	// 		}
	// 	}
    // }


	// async void WaitAndShow() {

	// 	await ToSignal(GetTree().CreateTimer(0.25f), "timeout");

	// 	this.Hide();
	// 	this.Dispose();

	// 	mainScene.currentScene.Resume();
	// 	mainScene.currentScene.UnsuspendSceneInput();
	// }

}
