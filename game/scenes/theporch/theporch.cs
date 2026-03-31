using Godot;

public partial class theporch : scene_script
{
    public void _on_exit_to_interior_body_entered(Node body)
    {
        mainScene.ChangeSceneToFile(Scenes.ENTRYWAY, "from_theporch");
    }

    public void _on_exit_to_housefront_body_entered(Node body)
    {
        mainScene.ChangeSceneToFile(Scenes.HOUSEFRONT, "from_theporch");
    }
}
