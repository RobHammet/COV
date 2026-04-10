using Godot;
using System;
using System.Collections.Generic;

public partial class DialogChoices : VBoxContainer
{
    public List<RichTextLabel> choices = new List<RichTextLabel>();
    public int currentChoice = -1;

    public override void _Ready()
    {
        currentChoice = -1;
    }

    public void InitDialogChoices(string[] _dialogChoices) {
        choices.Clear();

        for (int i = 0; i < _dialogChoices.Length; i++) {
            RichTextLabel newChoiceRTL = new RichTextLabel();
            newChoiceRTL.FitContent = true;
            newChoiceRTL.Text = _dialogChoices[i];
            newChoiceRTL.Name = i.ToString();
            int index = i;
            newChoiceRTL.Connect("mouse_entered", Callable.From(() => _on_dialog_choice_mouse_entered(index)));
            newChoiceRTL.Connect("mouse_exited",  Callable.From(_on_dialog_choice_mouse_exited));
            newChoiceRTL.Connect("gui_input", Callable.From((InputEvent e) => {
                if (e is InputEventScreenTouch touch && touch.Pressed)
                {
                    _on_dialog_choice_mouse_entered(index);
                    GetParent<DialogBox>()?.CloseThisDialog(index);
                }
            }));
            AddChild(newChoiceRTL);
            newChoiceRTL.Show();
            choices.Add(newChoiceRTL);
        }
    }

    public void _on_dialog_choice_mouse_entered(int pt = -1) {
        foreach (RichTextLabel choice in choices) {
            if (Int16.Parse(choice.Name) != pt)
                choice.AddThemeColorOverride("default_color", Colors.Black);
        }
        if (pt != -1) {
            choices[pt].AddThemeColorOverride("default_color", Colors.Red);
            currentChoice = pt;
        }
    }

    public void _on_dialog_choice_mouse_exited() {
        if (currentChoice != -1)
            choices[currentChoice].AddThemeColorOverride("default_color", Colors.Black);
        currentChoice = -1;
    }
}
