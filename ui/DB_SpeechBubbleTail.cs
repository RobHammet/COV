using Godot;
using System;

public partial class DB_SpeechBubbleTail : Polygon2D
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		// this.QueueRedraw();
	}

  	public override void _Draw() {
		// this.DrawPolyline(this.GetNode<Polygon2D>("DB_SpeechBubbleTail").Polygon, Colors.Black, 2, true);
    }

    public void DrawTail(){ 
        
    }
}
