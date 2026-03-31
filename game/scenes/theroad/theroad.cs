using Godot;
using System;

public partial class theroad : scene_script
{
	// Declare member variables here. Examples:
	// private int a = 2;
	// private string b = "text";

	// Called when the node enters the scene tree for the first time.
	// CollisionShape2D exitToHouseArea;
	public override void _Ready()
	{
		// exitToHouseArea = GetNode<Area2D>("exit_to_house").GetNode<CollisionShape2D>("CollisionShape2D");
	}

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
//  public override void _Process(float delta)
//  {
//      
//  }

	public void _on_exit_to_house_body_entered(Node body) {
		GD.Print(body);
		// Node2D bodyParent = body.GetParent() as Node2D;		
		// Vector2[] pts = {exitToHouseArea.Shape.GetRect().Position,
		// 				new Vector2(exitToHouseArea.Shape.GetRect().Position.X + exitToHouseArea.Shape.GetRect().Size.X, exitToHouseArea.Shape.GetRect().Position.Y),
		// 				exitToHouseArea.Shape.GetRect().Position + exitToHouseArea.Shape.GetRect().Size,
		// 				new Vector2(exitToHouseArea.Shape.GetRect().Position.X, exitToHouseArea.Shape.GetRect().Position.Y + exitToHouseArea.Shape.GetRect().Size.Y)
		// 				};
		// for (int i = 0; i < 4; i++) {
		// 	pts[i] += exitToHouseArea.GlobalPosition;
		// 	GD.Print(pts[i]);
		// }
		// GD.Print(bodyParent.Position);
		// if (Geometry2D.IsPointInPolygon(bodyParent.Position, pts)){
			if (body.GetParent().Name == "Character")
				mainScene.ChangeSceneToFile(Scenes.HOUSEFRONT);
		// }
	}

}
