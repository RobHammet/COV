using Godot;
using System.Collections.Generic;

public partial class DialogChoices : VBoxContainer
{
    public List<RichTextLabel> choices = [];
    public int currentChoice = -1;

    public override void _Ready()
    {
        currentChoice = -1;
    }

    public override void _Process(double delta)
    {
        if (choices.Count == 0 || !IsVisibleInTree()) return;
        Vector2 mouse = GetGlobalMousePosition();
        int hovered = -1;
        for (int i = 0; i < choices.Count; i++)
        {
            if (choices[i].GetGlobalRect().HasPoint(mouse))
            {
                hovered = i;
                break;
            }
        }
        if (hovered != currentChoice)
        {
            if (currentChoice >= 0 && currentChoice < choices.Count)
                choices[currentChoice].AddThemeColorOverride("default_color", Colors.Black);
            currentChoice = hovered;
            if (currentChoice >= 0)
                choices[currentChoice].AddThemeColorOverride("default_color", Colors.Red);
        }
    }

public void InitDialogChoices(string[] _dialogChoices) {
        choices.Clear();

        for (int i = 0; i < _dialogChoices.Length; i++) {
            RichTextLabel newChoiceRTL = new()
            {
                FitContent  = true,
                MouseFilter = MouseFilterEnum.Ignore,
                Text        = _dialogChoices[i],
                Name        = i.ToString()
            };
            AddChild(newChoiceRTL);
            newChoiceRTL.Show();
            choices.Add(newChoiceRTL);
        }
    }
}
