using Godot;
using System;

public partial class sittingroom : scene_script
{
    public override void _Ready()
    {
        base._Ready();

        if ((bool)GetFlag("keyTaken").Value == true)
            this.GetNode<thing>("key").Disappear();

        thing teddy = GetNode<thing>("teddy");
        Vector2[] navPoly = GetNode<NavigationRegion2D>("NavigationRegion2D").NavigationPolygon.GetOutline(0);

        float x = 0, y = 0;
        while (!Geometry2D.IsPointInPolygon(new Vector2(x, y), navPoly))
        {
            RandomNumberGenerator rng = new RandomNumberGenerator();
            rng.Randomize();
            x = rng.Randf() * this.GetViewportRect().Size.X;
            y = rng.Randf() * this.GetViewportRect().Size.Y;
        }
        teddy.Position = new Vector2(x, y);
    }

    public void _on_exit_to_thekitchen_body_entered(Node body)
    {
        mainScene.ChangeSceneToFile(Scenes.KITCHEN, "from_sittingroom");
    }

    public void _on_exit_to_upstairs_body_entered(Node body)
    {
        mainScene.ChangeSceneToFile(Scenes.UPSTAIRS, "from_sittingroom");
    }
}
