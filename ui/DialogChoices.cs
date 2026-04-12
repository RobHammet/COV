using Godot;
using System.Collections.Generic;

public partial class DialogChoices : VBoxContainer
{
    public List<RichTextLabel> choices = new List<RichTextLabel>();
    public int currentChoice = -1;

    public override void _Ready()
    {
        currentChoice = -1;
    }

    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what == NotificationSortChildren && choices.Count > 0)
        {
            GD.Print($"[DialogChoices] VBox sorted — own rect: pos={GlobalPosition} size={Size}");
            for (int i = 0; i < choices.Count; i++)
                GD.Print($"  choice[{i}] '{choices[i].Text}' pos={choices[i].Position} size={choices[i].Size} globalPos={choices[i].GlobalPosition}");
        }
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

    public override void _Input(InputEvent @event)
    {
        if (!IsVisibleInTree() || choices.Count == 0) return;
        if (@event is InputEventScreenTouch touch && touch.Pressed)
        {
            Vector2 canvasPos = GetViewport().GetScreenTransform().AffineInverse() * touch.Position;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].GetGlobalRect().HasPoint(canvasPos))
                {
                    currentChoice = i;
                    GetParent<DialogBox>()?.CloseThisDialog(i);
                    GetViewport().SetInputAsHandled();
                    break;
                }
            }
        }
    }

    public void InitDialogChoices(string[] _dialogChoices) {
        choices.Clear();

        for (int i = 0; i < _dialogChoices.Length; i++) {
            RichTextLabel newChoiceRTL = new RichTextLabel();
            newChoiceRTL.FitContent = true;
            newChoiceRTL.MouseFilter = MouseFilterEnum.Ignore;
            newChoiceRTL.Text = _dialogChoices[i];
            newChoiceRTL.Name = i.ToString();
            AddChild(newChoiceRTL);
            newChoiceRTL.Show();
            choices.Add(newChoiceRTL);
        }
    }
}
