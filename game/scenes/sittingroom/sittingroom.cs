using Godot;
using System;

public partial class sittingroom : scene_script
{
    // Declare member variables here. Examples:
    // private int a = 2;
    // private string b = "text";

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        base._Ready();
        
        if ((bool)GetFlag("keyTaken").Value == true) {
            this.GetNode<thing>("key").Disappear();
        }

        thing teddy = GetNode<thing>("teddy");
        Vector2[] navPoly = GetNode<NavigationRegion2D>("NavigationRegion2D").NavigationPolygon.GetOutline(0);

        float x = 0, y = 0;
        while (!Geometry2D.IsPointInPolygon(new Vector2(x,y), navPoly )) {
            RandomNumberGenerator rng = new RandomNumberGenerator();
            rng.Randomize();
            x = rng.Randf() * this.GetViewportRect().Size.X ; 
            int xi = (int)x;
            y = rng.Randf() * this.GetViewportRect().Size.Y ; 
            int yi = (int)y;
        }
        teddy.Position = new Vector2(x,y);



    }

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
//  public override void _Process(float delta)
//  {
//      
//  }

    public void _on_exit_to_thekitchen_body_entered(Node body) {
        
        GD.Print(body);

        mainScene.ChangeSceneToFile(Scenes.KITCHEN, null, Character.Direction.left);
    }

    public void _on_exit_to_upstairs_body_entered(Node body) {
        
        GD.Print(body);

        mainScene.ChangeSceneToFile(Scenes.UPSTAIRS, null, Character.Direction.left);
    }
}
