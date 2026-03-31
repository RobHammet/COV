using Godot;

public partial class kitchen : scene_script
{
    public void _on_exit_to_thesittingroom_body_entered(Node body)
    {
        mainScene.ChangeSceneToFile(Scenes.SITTINGROOM, "from_kitchen");
    }
}
