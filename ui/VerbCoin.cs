using Godot;

public partial class VerbCoin : Control
{
    public scene_script parentScene;

    private TextureButton _lookButton;
    private TextureButton _talkButton;
    private TextureButton _useButton;
    private TextureButton _itemButton;

    public override void _Ready()
    {
        parentScene = GetParent<scene_script>();

        var node2d = GetNode<Node2D>("VerbCoin_Node2D");
        _lookButton = node2d.GetNode<TextureButton>("LookButton");
        _talkButton = node2d.GetNode<TextureButton>("TalkButton");
        _useButton  = node2d.GetNode<TextureButton>("UseButton");
        _itemButton = node2d.GetNode<TextureButton>("ItemButton");

        parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);
    }

    // Desktop — mouse hover
    public void _on_LookButton_mouse_entered() => parentScene.mainScene.SetInteractMode(Globals.InteractModes.look);
    public void _on_LookButton_mouse_exited()  => parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);
    public void _on_TalkButton_mouse_entered() => parentScene.mainScene.SetInteractMode(Globals.InteractModes.talk);
    public void _on_TalkButton_mouse_exited()  => parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);
    public void _on_UseButton_mouse_entered()  => parentScene.mainScene.SetInteractMode(Globals.InteractModes.use);
    public void _on_UseButton_mouse_exited()   => parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);
    public void _on_ItemButton_mouse_entered() => parentScene.mainScene.SetInteractMode(Globals.InteractModes.item);
    public void _on_ItemButton_mouse_exited()  => parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);

    // Mobile — drag to select
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventScreenDrag drag)
            UpdateModeFromTouchPos(drag.Position);
    }

    private void UpdateModeFromTouchPos(Vector2 globalPos)
    {
        if (_lookButton.GetGlobalRect().HasPoint(globalPos))
            parentScene.mainScene.SetInteractMode(Globals.InteractModes.look);
        else if (_talkButton.GetGlobalRect().HasPoint(globalPos))
            parentScene.mainScene.SetInteractMode(Globals.InteractModes.talk);
        else if (_useButton.GetGlobalRect().HasPoint(globalPos))
            parentScene.mainScene.SetInteractMode(Globals.InteractModes.use);
        else if (_itemButton.GetGlobalRect().HasPoint(globalPos))
            parentScene.mainScene.SetInteractMode(Globals.InteractModes.item);
        else
            parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);
    }
}
