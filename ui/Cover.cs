// Cover.cs — cover page shown before the first game scene.
//
// Extends scene_script so MainScene.currentScene (typed scene_script) can
// hold it without any type changes to MainScene. Two overrides keep it safe:
//   _EnterTree: sets isInsert=true before base runs, preventing ego spawn.
//   _UnhandledInput: click-anywhere triggers PageTurn to the start scene.
//
// Visual setup: add a ColorRect (full viewport, black) and your cover
// Sprite2D as children in the editor. No camera, no NavMesh, no ego needed.

using Godot;
using System.Text.Json.Nodes;

public partial class Cover : scene_script
{
    private string _startScene = "";

    public override void _EnterTree()
    {
        isInsert         = true;   // prevent ego spawning in base._EnterTree()
        hasCameraControl = false;
        base._EnterTree();
    }

    public override void _Ready()
    {
        base._Ready();  // safe: loads DialogBox prefab, SetupAreaZones returns early (no JSON)

        string json = FileAccess.GetFileAsString("res://game/game_config.json");
        _startScene = JsonNode.Parse(json)?.AsObject()?["start_scene"]?.GetValue<string>() ?? "";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (mainScene?.isInTransition == true) return;
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (!string.IsNullOrEmpty(_startScene))
            {
                mainScene.ChangeSceneToFile(_startScene, default, TransitionType.CoverTurn);
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
