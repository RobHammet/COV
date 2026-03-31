using Godot;

public partial class upstairs : scene_script
{
    public void _on_exit_to_sittingroom_body_entered(Node body)
    {
        mainScene.ChangeSceneToFile(Scenes.SITTINGROOM, "from_upstairs");
    }
}
