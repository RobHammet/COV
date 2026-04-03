using Godot;
using System;

public partial class fridge : thing
{
	// Called when the node enters the scene tree for the first time.
	// public override void _Ready()
	// {
	// }

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }

	public override bool SpecificUse(NPC character, EventSequence eventSequence) {
		
	
		// if ((bool)parentScene.GetFlag("keyTaken").Value == true) {

		// 	eventSequence.AddEventSpeak(character, "GOOD THING I HAVE THIS KEY.", new Vector2(0,0), true);
		// 	eventSequence.AddEventRemoveFromInventory(InventoryItem.ItemType.key);
		// } else {
			eventSequence.AddEventSpeak(character, "CAN'T USE THAT WITHOUT SOME KIND OF KEY.", new Vector2(0,0), true);
		// }

       

        return true;
    }

	public override bool SpecificUseItem(InventoryItem.ItemType item) {	

		switch (item) {
			case InventoryItem.ItemType.key:
				parentScene.eventQueue.AddEventSpeak(parentScene.ego, "YAY! I OPENED IT WITH THE KEY!", new Vector2(0,0), true);
				return true;
				break;
			default:
				return false;
				break;
		}
    }


}
