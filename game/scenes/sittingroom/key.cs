using Godot;
using System;

public partial class key : thing
{
   
    public override bool SpecificUse(Character character, EventSequence eventSequence) {

        eventSequence.AddEventMove(character, this.interactPoint, true);
        eventSequence.AddEventSpeak(character, "I SHOULD PROBABLY TAKE THIS KEY.", new Vector2(0,0), true);
        eventSequence.AddEventAddToInventory(this);

        eventSequence.AddEventAddFlag(new Globals.SceneFlag(parentScene.Name, "keyTaken", true));



        return true;
    }
}
