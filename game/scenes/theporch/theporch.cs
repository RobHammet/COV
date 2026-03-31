using Godot;
using System;

public partial class theporch : scene_script
{

    public void _on_exit_to_interior_body_entered (Node body) {

        EventSequence entrywaySequence = new EventSequence(this);
		Character character = this.GetNode<Character>("Character");
        entrywaySequence.AddEventChangeFacing(character, Character.Direction.down, false);
        entrywaySequence.AddEventNarrate("HE CAREFULLY WALKED INTO THE HOUSE...", Vector2.Zero, false);
        entrywaySequence.AddEventMove(character, new Vector2(200, 465), false);
        entrywaySequence.AddEventChangeFacing(character, Character.Direction.left, false);
        entrywaySequence.AddEventNarrate("LOOKING IN EVERY DIRECTION...", new Vector2(300, 400), false);
        entrywaySequence.AddEventMove(character, new Vector2(600, 600), false);


        mainScene.ChangeSceneToFile(Scenes.ENTRYWAY,null,Character.Direction.down, entrywaySequence);        
    }

    public void _on_exit_to_housefront_body_entered (Node body) {

        EventSequence walkdownstepsSequence = new EventSequence(this);
		Character character = this.GetNode<Character>("Character");
        walkdownstepsSequence.AddEventMove(character, new Vector2(351,490), true);
        mainScene.ChangeSceneToFile(Scenes.HOUSEFRONT,new Vector2(351,450),Character.Direction.down, walkdownstepsSequence);        
    }
}
