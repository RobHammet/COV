using Godot;
using System;

public partial class porch_door : thing
{
   
    public override bool SpecificUse(Character character, EventSequence eventSequence) {
        GD.Print("CLICKED PORCH DOOR");


        if ((bool)parentScene.GetFlag("porchDoorOpen").Value == false) {
            eventSequence.AddEventMove(character, this.interactPoint, true);
            eventSequence.AddEventChangeFacing(character, Character.Direction.up);
            eventSequence.AddEventSpeak(character, "MAYBE NO ONE WOULD MIND IF \n I JUST GO IN...", new Vector2(0,0), true);

            eventSequence.AddEventPlayAnimation(this.GetNode<Sprite2D>("Sprite2D").GetNode<AnimationPlayer>("AnimationPlayer"), "open");

            eventSequence.AddEventAddFlag(new Globals.SceneFlag(parentScene.Name, "porchDoorOpen", true));

            NavigationRegion2D navReg2D = this.GetParent().GetNode<NavigationRegion2D>("NavigationRegion2D");
            Polygon2D newPoly = navReg2D.GetNode<Polygon2D>("Polygon2D");
            var polygon = new NavigationPolygon();       
            polygon.AddOutline(newPoly.Polygon);
            polygon.MakePolygonsFromOutlines();
            navReg2D.NavigationPolygon = polygon;

        } else {
            eventSequence.AddEventSpeak(character, "IT'S ALREADY OPEN.", new Vector2(0,0), true);
        }

        // string[] newLookText = new string[1];
        // newLookText[0] = "IT'S OPEN NOW.";
        // eventSequence.AddEventChangeLookText(this, newLookText);

        return true;
    }


}
