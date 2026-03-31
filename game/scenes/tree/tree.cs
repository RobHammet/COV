using Godot;

public partial class tree : scene_script
{
    public void _on_exit_to_housefront_body_entered(Node2D body)
    {
        mainScene.ChangeSceneToFile(Scenes.HOUSEFRONT, "from_tree");
    }
}
