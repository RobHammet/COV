using Godot;
using System;

[GlobalClass]
public partial class DialogAction : Node
{
	

	[Export] public bool useCustomAction = false;

	[Export] public DialogNode choiceToHide = null;
	[Export] public DialogNode choiceToShow = null;

	[Export] public string flagName;
	[Export] public string flagValue;



	[Export] Event.EventType type = Event.EventType.none;

    [Export] public string Phrase { get; set; }

    //public string[] dialogChoices;

    //public string dialogTreeNameInScene;
    [Export] public Vector2 eventPosition { get; set; }
    //public EventType Type;

    //[Export] public Color Color;

    // public bool IsFinished { get; set; }
    // public bool IsInProgress { get; set; }

    [Export] public bool IsInterruptable { get; set; }
    [Export] public NPC ActingCharacter;

    [Export] public AnimationPlayer animationPlayer;
    [Export] public string animationToPlay;
    public scene_script ParentScene;

    [Export] public NPC.Direction NewFacing;

    [Export] public thing interactThing;
    public InventoryItem.ItemType itemType;

    //public Globals.SceneFlag sceneFlag;

	public override void _Ready()
	{
		ParentScene = GetSceneScript(this); // this.GetParent().GetParent().GetParent() as scene_script;
		//GD.Print("My parent is " + ParentScene.Name);
	}

	public scene_script GetSceneScript(Node fromNode) {
		if (fromNode.GetParent() is scene_script) {
			return (scene_script)fromNode.GetParent();
		} else {
			return GetSceneScript((Node)fromNode.GetParent());
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void DoAction() {
		GD.Print("DoAction called");
		GD.Print("flagName:" + flagName);
		if (useCustomAction) {
			CustomAction();
		}
		if (this.flagName != null) {
			// add a flag
			if (flagValue == "true" || flagName == "false" ) {
				bool flagBoolValue = (flagValue == "true" ? true: false);
				ParentScene.AddFlag(flagName, flagBoolValue);
			} else {
				ParentScene.AddFlag(flagName, flagValue);
			}
		} 
		if (choiceToHide != null) {
			choiceToHide.isHidden = true;
		}
		if (choiceToShow != null) {
			choiceToShow.isHidden = false;
		}
		if (this.type != Event.EventType.none ) {
			GD.Print("supposed to add event here.....");
			// add an event

			Event newEvent = new Event();
			newEvent.Type = this.type;
			newEvent.Phrase = this.Phrase;
			newEvent.Position = this.eventPosition;
			newEvent.IsInterruptable = this.IsInterruptable;
			newEvent.ActingCharacter = this.ActingCharacter;
			newEvent.animationPlayer = this.animationPlayer;
			newEvent.animationToPlay = this.animationToPlay;
			newEvent.ParentScene = this.ParentScene;
			newEvent.NewFacing = this.NewFacing;
			newEvent.interactThing = this.interactThing;
			newEvent.itemType = this.itemType;

			ParentScene.eventQueue.AddEvent(newEvent);

			// EventSequence newEvents = null;
			// newEvents = new EventSequence(ParentScene);
			// newEvents.AddEvent(newEvent); //new string[] { "fuuu", "ckkkk", "youuuu"},null,true
			// ParentScene.eventQueue = null;
			// ParentScene.eventQueue = newEvents;


		}


	}


	 public virtual void CustomAction() {
        
    }

}
