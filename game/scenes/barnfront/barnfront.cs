using Godot;

public partial class barnfront : scene_script
{
    public void _on_exit_to_housefront_body_entered(Node2D body)
    {
        mainScene.ChangeSceneToFile(Scenes.HOUSEFRONT, "from_barnfront");
    }

    public void _on_exit_to_barninterior_body_entered(Node2D body)
    {
        mainScene.ChangeSceneToFile(Scenes.BARNINTERIOR, "from_barnfront");
    }
}
