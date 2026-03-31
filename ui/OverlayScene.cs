using Godot;
using System;

public partial class OverlayScene : CanvasLayer
{
    private Label label;
    private MainScene mainScene;
    public VerbPanel verbPanel;

    public override void _Ready()
    {
        label     = GetNode<Label>("CelBorder/Label");
        mainScene = (MainScene)GetTree().Root.GetChild(1);
        verbPanel = GetNode<VerbPanel>("VerbPanel");

        Vector2 vp = GetViewport().GetVisibleRect().Size;

        // Scale CelBorder sprite to cover the viewport exactly.
        var celBorder  = GetNode<Sprite2D>("CelBorder");
        celBorder.Position = vp / 2f;
        celBorder.Scale    = vp / celBorder.Texture.GetSize();

        // Snap VerbPanel to top of viewport, full width, no extra scale.
        verbPanel.Scale        = Vector2.One;
        verbPanel.AnchorLeft   = 0f;
        verbPanel.AnchorTop    = 0f;
        verbPanel.AnchorRight  = 1f;
        verbPanel.AnchorBottom = 0f;
        verbPanel.OffsetLeft   = 0f;
        verbPanel.OffsetRight  = 0f;
        verbPanel.OffsetTop    = 0f;
        verbPanel.OffsetBottom = 100f;
    }

    public void _on_Button_pressed()
    {
        GD.Print("Button pressed.");
        GetViewport().SetInputAsHandled();
    }

    public void SetLabelText(string text)
    {
        label.Text = text;
    }

    // Returns the inner boundary of the cel border in screen space.
    // CelBorderEdge is a CollisionShape2D child of CelBorder; its GlobalScale
    // already includes the parent's scale, so this is correct for any viewport size.
    public Rect2 GetInnerRect()
    {
        var edge     = GetNode<CollisionShape2D>("CelBorder/CelBorderEdge");
        Vector2 center   = edge.GlobalPosition;
        Vector2 halfSize = edge.Shape.GetRect().Size / 2f * edge.GlobalScale;
        return new Rect2(center - halfSize, halfSize * 2f);
    }
}
