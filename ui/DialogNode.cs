using Godot;
using System;
using System.Collections.Generic;


[GlobalClass]
public partial class DialogNode : Node2D
{
	[Export] public string displayAsChoice;
	[Export] public DialogNode toNode;
	[Export] public bool isHidden = false;

	public override void _Ready()
	{
	
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}


}
