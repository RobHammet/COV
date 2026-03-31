using Godot;

public partial class entryway : scene_script
{
    public void _on_into_the_house_body_entered(Node body)
    {
        mainScene.ChangeSceneToFile(Scenes.SITTINGROOM, "from_entryway");
    }
}
