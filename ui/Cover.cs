// Cover.cs — cover page shown before/after game scenes.
//
// Extends scene_script so MainScene.currentScene can hold it without type changes.
// Button clicks (NewGame / Load / Resume) are detected via _Input position checks —
// no signals used.  The old click-anywhere-to-start handler has been removed.

using Godot;
using System.Text.Json.Nodes;

public partial class Cover : scene_script
{
    private string  _startScene    = "";
    private Control _newGameBtn;
    private Control _saveGameBtn;
    private Control _loadGameBtn;
    private Control _resumeGameBtn;
    private Control _exitGameBtn;

    public override void _EnterTree()
    {
        isInsert         = true;   // prevent ego spawning in base._EnterTree()
        hasCameraControl = false;
        base._EnterTree();
    }

    public override void _Ready()
    {
        base._Ready();

        string json = FileAccess.GetFileAsString("res://game/game_config.json");
        _startScene = JsonNode.Parse(json)?.AsObject()?["start_scene"]?.GetValue<string>() ?? "";

        _newGameBtn    = FindChild("NewGameButton",    true, false) as Control;
        _saveGameBtn   = FindChild("SaveGameButton",   true, false) as Control;
        _loadGameBtn   = FindChild("LoadGameButton",   true, false) as Control;
        _resumeGameBtn = FindChild("ResumeGameButton", true, false) as Control;
        _exitGameBtn   = FindChild("ExitGameButton",   true, false) as Control;
        RefreshButtonStates();
    }

    public void RefreshButtonStates()
    {
        var dim   = new Color(1, 1, 1, 0.35f);
        var full  = new Color(1, 1, 1, 1);
        bool has  = mainScene?.HasResumeState == true;
        if (_saveGameBtn   != null) _saveGameBtn.Modulate   = has ? full : dim;
        if (_resumeGameBtn != null) _resumeGameBtn.Modulate = has ? full : dim;
    }

    public override void _Input(InputEvent @event)
    {
        if (mainScene?.isInTransition == true) return;

        Vector2? pos = null;
        if (@event is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            pos = GetGlobalMousePosition();
        else if (@event is InputEventScreenTouch st && !st.Pressed)
            pos = GetViewport().GetCanvasTransform().AffineInverse() * st.Position;

        if (!pos.HasValue) return;

        if (_newGameBtn != null && _newGameBtn.GetGlobalRect().HasPoint(pos.Value))
        {
            GetViewport().SetInputAsHandled();
            mainScene.NewGame(_startScene);
        }
        else if (_saveGameBtn != null && mainScene.HasResumeState
                 && _saveGameBtn.GetGlobalRect().HasPoint(pos.Value))
        {
            GetViewport().SetInputAsHandled();
            mainScene.Save();
        }
        else if (_loadGameBtn != null && _loadGameBtn.GetGlobalRect().HasPoint(pos.Value))
        {
            GetViewport().SetInputAsHandled();
            mainScene.Load();
        }
        else if (_resumeGameBtn != null && mainScene.HasResumeState
                 && _resumeGameBtn.GetGlobalRect().HasPoint(pos.Value))
        {
            GetViewport().SetInputAsHandled();
            mainScene.ResumeGame();
        }
        else if (_exitGameBtn != null && _exitGameBtn.GetGlobalRect().HasPoint(pos.Value))
        {
            GetViewport().SetInputAsHandled();
            GetTree().Quit();
        }
    }
}
