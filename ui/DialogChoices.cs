using Godot;
using System;
using System.Collections.Generic;

public partial class DialogChoices : VBoxContainer
{


    public List<RichTextLabel> choices = new List<RichTextLabel>();

	public int currentChoice = -1;
    
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		currentChoice = -1;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		
		Vector2 mousePos = GetGlobalMousePosition();
		foreach (RichTextLabel choice in choices) {
			if (choice.GetGlobalRect().HasPoint(mousePos)) {
				_on_dialog_choice_mouse_entered(Int16.Parse(choice.Name));
			}
		}
	}

	public void InitDialogChoices(string[] _dialogChoices) {
		
		choices.Clear();

		for (int i = 0; i < _dialogChoices.Length; i++) {

			 RichTextLabel newChoiceRTL = new RichTextLabel();
			GD.Print(_dialogChoices[i]);
			//newChoiceRTL.Size = new Vector2(242,21);
			newChoiceRTL.FitContent = true;
			newChoiceRTL.Text = _dialogChoices[i];
			newChoiceRTL.Name = i.ToString();
        	//newChoiceRTL.AddThemeColorOverride("default_color", Colors.Blue);
			newChoiceRTL.Connect("mouse_entered", new Callable(this, "_on_dialog_choice_mouse_entered"));
			newChoiceRTL.Connect("mouse_exited", new Callable(this, "_on_dialog_choice_mouse_exited"));
			AddChild(newChoiceRTL);
			newChoiceRTL.Show();
			choices.Add(newChoiceRTL);

			// choices[i].Text = _dialogChoices[i];
		}

		

	}

    public void _on_dialog_choice_mouse_entered(int pt = -1) {
		GD.Print("I was passed " + pt);
		//int pt = -1;
		foreach (RichTextLabel choice in choices) {
			if (Int16.Parse(choice.Name) != pt) {
				choice.AddThemeColorOverride("default_color", Colors.Black);
				//		pt = Int16.Parse(choice.Name);
			}
		}
        GD.Print("Choice " + pt);
		if (pt != -1) {
        	choices[pt].AddThemeColorOverride("default_color", Colors.Red);			
			currentChoice = pt;
		}
        // choices[1].AddThemeColorOverride("default_color", Colors.Black);
        // choices[2].AddThemeColorOverride("default_color", Colors.Black);

		// currentChoice = 1;
		//choices[0].QueueRedraw();
    }


	public void _on_dialog_choice_mouse_exited() {
		if (currentChoice != -1) {
        	choices[currentChoice].AddThemeColorOverride("default_color", Colors.Black);
		}
		currentChoice = -1;
	}

    // public void _on_dialog_choice_1_mouse_entered() {
    //     GD.Print("Choice 1");
    //     choices[0].AddThemeColorOverride("default_color", Colors.Red);
    //     // choices[1].AddThemeColorOverride("default_color", Colors.Black);
    //     // choices[2].AddThemeColorOverride("default_color", Colors.Black);

	// 	currentChoice = 1;
	// 	//choices[0].QueueRedraw();
    // }
	

    // public void _on_dialog_choice_2_mouse_entered() {
    //     GD.Print("Choice 2");
    //     // choices[0].AddThemeColorOverride("default_color", Colors.Black);
    //     choices[1].AddThemeColorOverride("default_color", Colors.Red);
    //     // choices[2].AddThemeColorOverride("default_color", Colors.Black);

	// 	currentChoice = 2;
	// 	//choices[1].QueueRedraw();
    // }

    // public void _on_dialog_choice_3_mouse_entered() {
    //     GD.Print("Choice 3");
    //     // choices[0].AddThemeColorOverride("default_color", Colors.Black);
    //     // choices[1].AddThemeColorOverride("default_color", Colors.Black);
    //     choices[2].AddThemeColorOverride("default_color", Colors.Red);

	// 	currentChoice = 3;
	// 	//choices[2].QueueRedraw();
    // }

	// public void _on_dialog_choice_1_mouse_exited() {
    //     choices[0].AddThemeColorOverride("default_color", Colors.Black);
	// 	currentChoice = 0;
	// }
	// public void _on_dialog_choice_2_mouse_exited() {
    //     choices[1].AddThemeColorOverride("default_color", Colors.Black);
	// 	currentChoice = 0;
	// }
	// public void _on_dialog_choice_3_mouse_exited() {
    //     choices[2].AddThemeColorOverride("default_color", Colors.Black);
	// 	currentChoice = 0;
	// }

}
