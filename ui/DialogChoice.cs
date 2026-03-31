using Godot;
using System;
using System.Collections.Generic;

[GlobalClass]

public partial class DialogChoice : Node
{
	// [Export] public string[] choices;
	// [Export] public DialogNode[] toNode;
	[Export] public DialogNode[] choices;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		List<DialogNode> choiceList = new List<DialogNode>();
		foreach (Node n in this.GetChildren()) {
			if (n is DialogNode) {
				choiceList.Add((DialogNode)n);
			}
		}
		this.choices = choiceList.ToArray();
	}

	// // Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }
}
