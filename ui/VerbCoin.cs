using Godot;
using System;

public partial class VerbCoin : Control
{
    // Declare member variables here. Examples:
    // private int a = 2;
    // private string b = "text";

    // Called when the node enters the scene tree for the first time.

    public scene_script parentScene;
   // public Globals.InteractModes chosenMode = Globals.InteractModes.walk;
    public override void _Ready()
    {
        parentScene = (scene_script)this.GetParent();
      //  chosenMode = Globals.InteractModes.walk;

        // parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);
        parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);


    }

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
//  public override void _Process(float delta)
//  {
//      
//  }

    public void _on_LookButton_mouse_entered() {
        parentScene.mainScene.SetInteractMode(Globals.InteractModes.look);
       // chosenMode = Globals.InteractModes.look;
    }

    public void _on_LookButton_mouse_exited() {
        parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);
      //  chosenMode = Globals.InteractModes.walk;
    }
}
