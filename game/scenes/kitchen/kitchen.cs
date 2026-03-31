using Godot;

public partial class kitchen : scene_script
{
    public void _on_exit_to_thesittingroom_body_entered(Node body)
    {
        mainScene.ChangeSceneToFile(Scenes.SITTINGROOM, new Vector2(310, 240), Character.Direction.down);
    }
}
