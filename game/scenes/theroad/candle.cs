using Godot;
using System;

public partial class candle : thing
{
    // Declare member variables here. Examples:
    // private int a = 2;
    // private string b = "text";

    // Called when the node enters the scene tree for the first time.
    // public override void _Ready()
    // {
    //     base._Ready();
    //     this.sprite.FlipH = true;   
    // }


//    public override bool SpecificLook(Character character, EventSequence eventSequence) {
//         GD.Print("LOOKIN AT THE CANDLE");

//         eventSequence.AddEventMove(character, this.interactPoint, true);
//         eventSequence.AddEventNarrate("IT'S A CANDLE.");
//         // string[] newLookText = new string[1];
//         // newLookText[0] = "OK, SO.... IT'S STILL A CANDLE.";
//         // eventSequence.AddEventChangeLookText(this, newLookText);
//         return true;
//     }


    public override bool SpecificUse(Character character, EventSequence eventSequence) {

     //   eventSequence.AddEventMove(character, this.interactPoint, true);
        eventSequence.AddEventThink(character, "MAYBE I'LL GO AHEAD AND LIGHT THIS CANDLE... LET'S SEE IF THAT WORKS.", new Vector2(0,0), true);
        eventSequence.AddEventThink(character, "I DUNNO.", new Vector2(0,0), true);
         eventSequence.AddEventThink(character, "TO BE HONEST, I DON'T USUALLY DO THIS KIND OF THING. I MEAN... IT'S A CANDLE JUST SITTING HERE ON THE SIDE OF THE ROAD. ON THE ONE HAND, IT'S JUST WEIRD AND I'D HAVE TO BE CRAZY TO ENGAGE WITH IT IN ANY WAY.", new Vector2(0,0), true);
         eventSequence.AddEventThink(character, "SCREW IT.", new Vector2(0,0), true);


        eventSequence.AddEventPlayAnimation(this.GetNode<Sprite2D>("Sprite2D").GetNode<AnimationPlayer>("AnimationPlayer"), "burn");


        eventSequence.AddEventAddToInventory(this);

        return true;
    }

    
//  // Called every frame. 'delta' is the elapsed time since the previous frame.
//  public override void _Process(float delta)
//  {
//      
//  }
}
