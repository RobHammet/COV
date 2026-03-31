using Godot;

public partial class barninterior : scene_script
{
    public override void _Ready()
    {
        base._Ready();
        if ((bool)GetFlag("axeTaken").Value == true)
            this.GetNode<thing>("axe").Disappear();
    }

    public void _on_exit_to_barnfront_body_entered(Node2D body)
    {
        mainScene.ChangeSceneToFile(Scenes.BARNFRONT, "from_barninterior");
    }
}
