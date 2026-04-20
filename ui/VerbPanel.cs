using Godot;
using System;

public partial class VerbPanel : Control
{
    public MainScene mainScene;
    public TextureButton itemButton;
   // public Globals.InteractModes chosenMode = Globals.InteractModes.walk;
    public override void _Ready()
    {
        mainScene = (MainScene)this.GetParent().GetParent().GetParent();

        itemButton = this.GetNode<Node2D>("VerbPanel_Node2D").GetNode<TextureButton>("ItemButton");
      //  chosenMode = Globals.InteractModes.walk;

        // parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);
      //  parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);


    }

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
//  public override void _Process(float delta)
//  {
//      
//  }

    public void _on_LookButton_pressed() {
        GD.Print("_on_LookButton_pressed");
        mainScene.SetInteractMode(Globals.InteractModes.look);
    }
    public void _on_use_button_pressed() {
        GD.Print("_on_use_button_pressed");
        mainScene.SetInteractMode(Globals.InteractModes.use);
    }
    public void _on_talk_button_pressed() {
        GD.Print("_on_use_button_pressed");
        mainScene.SetInteractMode(Globals.InteractModes.talk);
    }
    public void _on_walk_button_pressed() {
        GD.Print("_on_use_button_pressed");
        mainScene.SetInteractMode(Globals.InteractModes.walk);
    }

    public void _on_item_button_pressed() {
        mainScene.SetInteractMode(Globals.InteractModes.item);
    }

    public void _on_inventory_button_pressed() {
        mainScene.currentScene?.ToggleInventory();
    }
}
