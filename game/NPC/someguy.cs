using Godot;
using System;

public partial class someguy : NPC
{
	// // Called when the node enters the scene tree for the first time.
	// public override void _Ready()
	// {
	// }

	// // Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }

    public override bool SpecificUseItem(InventoryItem.ItemType item) {
        if (item == InventoryItem.ItemType.key) {
            parentScene.eventQueue.AddEventSpeak(this, "NO, THANKS. I GOT A KEY JUST LIKE THAT AT HOME.", Vector2.Zero, true);
            return true;
        }

        return false;
    }

    public override bool SpecificTalk(Character character, EventSequence eventSequence) {

        
        // eventSequence.AddEventSpeak(character, "HEY, THERE!", new Vector2(0,0));
        // eventSequence.AddEventSpeak(this, "HEY, YOU!", new Vector2(0,0));
        // eventSequence.AddEventMove(this, new Vector2(230, 246), true);
       
        eventSequence.AddEventConversation("ChatWithFarmer", null, true); 


    //    GD.Print(parentScene.GetFlag("talkedToFarmer").Value);
    //    if ((bool)parentScene.GetFlag("talkedToFarmer").Value == false) {
    //         GD.Print("YES IT IS INDEED FALSE!!!!!!");
    //         eventSequence.AddEventConversation("ChatWithFarmer", null, true); 
    //         parentScene.AddFlag("talkedToFarmer", true);
    //    } else {
    //         eventSequence.AddEventSpeak(this, "I'VE SAID ENOUGH.", new Vector2(0,0));
    //    }
       






        return true;
    }

}
