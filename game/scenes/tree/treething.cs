using Godot;
using System;

public partial class treething : thing
{
	// Called when the node enters the scene tree for the first time.
	// public override void _Ready()
	// {
	// }

	// // Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }

	public override bool SpecificUse(Character character, EventSequence eventSequence) {
		
	
		// if ((bool)parentScene.GetFlag("keyTaken").Value == true) {

		// 	eventSequence.AddEventSpeak(character, "GOOD THING I HAVE THIS KEY.", new Vector2(0,0), true);
		// 	eventSequence.AddEventRemoveFromInventory(InventoryItem.ItemType.key);
		// } else {
			eventSequence.AddEventSpeak(character, "CAN'T JUST PULL THIS DOWN WITH MY HANDS.");
		// }

       

        return true;
    }

	public override bool SpecificUseItem(InventoryItem.ItemType item) {	

		switch (item) {
			case InventoryItem.ItemType.axe:
				if ((bool)parentScene.GetFlag("talkedAboutTree").Value == true) {
					parentScene.eventQueue.AddEventSpeak(parentScene.character, "YAY! I CHOPPED IT WITH AN AXE!");					
				} else {
					parentScene.eventQueue.AddEventSpeak(parentScene.character, "I DON'T HAVE A REASON TO DO THAT RIGHT NOW.");
				}
				return true;
				break;
			default:
				return false;
				break;
		}
    }

}
