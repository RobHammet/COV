using Godot;

[GlobalClass]
public partial class DialogAction : Node
{
    // The subset of step types a DialogAction can trigger.
    public enum ActionType { none, speak, think, narrate, change_facing, play_animation }

    [Export] public bool       useCustomAction = false;
    [Export] public DialogNode choiceToHide    = null;
    [Export] public DialogNode choiceToShow    = null;
    [Export] public string     flagName;
    [Export] public string     flagValue;

    [Export] public ActionType    type           = ActionType.none;
    [Export] public string        Phrase         { get; set; }
    [Export] public Vector2       eventPosition  { get; set; }
    [Export] public bool          IsInterruptable { get; set; }
    [Export] public NPC           ActingCharacter;
    [Export] public AnimationPlayer animationPlayer;
    [Export] public string        animationToPlay;
    [Export] public NPC.Direction NewFacing;
    [Export] public thing         interactThing;

    public scene_script ParentScene;

    public override void _Ready()
    {
        ParentScene = GetSceneScript(this);
    }

    public scene_script GetSceneScript(Node fromNode)
    {
        if (fromNode.GetParent() is scene_script s) return s;
        return GetSceneScript(fromNode.GetParent());
    }

    public override void _Process(double delta) { }

    public void DoAction()
    {
        if (useCustomAction)
            CustomAction();

        if (flagName != null)
        {
            bool isBool = flagValue == "true" || flagValue == "false";
            if (isBool)
                ParentScene.AddFlag(flagName, flagValue == "true");
            else
                ParentScene.AddFlag(flagName, flagValue);
        }

        if (choiceToHide != null) choiceToHide.isHidden = true;
        if (choiceToShow != null) choiceToShow.isHidden = false;

        if (type != ActionType.none)
        {
            switch (type)
            {
                case ActionType.speak:
                    if (ActingCharacter != null)
                        ParentScene.eventQueue.AddEventSpeak(ActingCharacter, Phrase, eventPosition, IsInterruptable);
                    break;
                case ActionType.think:
                    if (ActingCharacter != null)
                        ParentScene.eventQueue.AddEventThink(ActingCharacter, Phrase, eventPosition, IsInterruptable);
                    break;
                case ActionType.narrate:
                    ParentScene.eventQueue.AddEventNarrate(Phrase, eventPosition, IsInterruptable);
                    break;
                case ActionType.change_facing:
                    if (ActingCharacter != null)
                        ParentScene.eventQueue.AddEventChangeFacing(ActingCharacter, NewFacing, IsInterruptable);
                    break;
                case ActionType.play_animation:
                    if (animationPlayer != null)
                        ParentScene.eventQueue.AddEventPlayAnimation(animationPlayer, animationToPlay, IsInterruptable);
                    break;
            }
        }
    }

    public virtual void CustomAction() { }
}
