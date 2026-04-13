using Godot;

public partial class OverlayScene : CanvasLayer
{
    public enum CelBorderStyle { Normal = 0, Circle = 1, Wavy = 2 }

    [Export] public float          BorderWidth  = 64f;  // white outer border in pixels (drives inner rect)
    [Export] public float          LineWidth    = 4f;
    [Export] public float          CornerRadius = 48f;
    [Export] public float          Softness     = 8f;
    [Export] public CelBorderStyle BorderStyle  = CelBorderStyle.Normal;

    private Label     label;
    private MainScene mainScene;
    public  VerbPanel verbPanel;
    private ColorRect _celBorder;

    public override void _Ready()
    {
        label     = GetNode<Label>("CelBorder/Label");
        mainScene = (MainScene)GetTree().Root.GetChild(1);
        verbPanel = GetNode<VerbPanel>("VerbPanel");
        _celBorder = GetNode<ColorRect>("CelBorder");

        Vector2 vp = GetViewport().GetVisibleRect().Size;

        _celBorder.Position = Vector2.Zero;
        _celBorder.Size     = vp;
        _celBorder.Material = MakeBorderMaterial(GetInnerRect());

        // Sync the InnerWindow visual guide to match BorderWidth.
        var inner = GetNode<Control>("CelBorder/InnerWindow");
        inner.Position = Vector2.One * BorderWidth;
        inner.Size     = vp - Vector2.One * BorderWidth * 2f;

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

    public void OnButtonPressed()
    {
        GD.Print("Button pressed.");
        GetViewport().SetInputAsHandled();
    }

    public void SetLabelText(string text)
    {
        label.Text = text;
    }

    public void HideForTransition()   => _celBorder?.Hide();
    public void ShowAfterTransition() => _celBorder?.Show();

    public void SetBorderStyle(CelBorderStyle style)
    {
        BorderStyle = style;
        if (_celBorder?.Material is ShaderMaterial mat)
            mat.SetShaderParameter("border_style", (int)style);
    }

    public void SetBorderWidth(float bw)
    {
        BorderWidth = bw;
        if (_celBorder?.Material is ShaderMaterial mat)
        {
            Rect2 r = GetInnerRect();
            mat.SetShaderParameter("inner_rect", new Vector4(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y));
        }
    }

    public void SetBorderStyle(string style) => SetBorderStyle(ParseBorderStyle(style));

    public static CelBorderStyle ParseBorderStyle(string style) =>
        style switch {
            "circle" => CelBorderStyle.Circle,
            "wavy"   => CelBorderStyle.Wavy,
            _        => CelBorderStyle.Normal,
        };

    // Returns the inner transparent window rect in screen space.
    public Rect2 GetInnerRect()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        return new Rect2(Vector2.One * BorderWidth, vp - Vector2.One * BorderWidth * 2f);
    }

    // Creates a cel border overlay for a transition panel thumbnail.
    // Uses UV-space math so it's immune to screen position and animation scale.
    // style and borderWidth override the scene-level defaults when non-null.
    public ColorRect MakePanelBorderOverlay(Vector2 position, Vector2 size,
        CelBorderStyle? style = null, float? borderWidth = null)
    {
        var mat = new ShaderMaterial
        {
            Shader = ResourceLoader.Load<Shader>("res://shaders/cel_border_panel_shader.gdshader")
        };
        float bw    = borderWidth ?? BorderWidth;
        float scale = size.X / GetViewport().GetVisibleRect().Size.X;
        mat.SetShaderParameter("panel_size",    new Vector2(size.X, size.Y));
        mat.SetShaderParameter("border_width",  bw             * scale);
        mat.SetShaderParameter("line_width",    LineWidth      * scale);
        mat.SetShaderParameter("corner_radius", CornerRadius   * scale);
        mat.SetShaderParameter("softness",      Softness       * scale);
        mat.SetShaderParameter("border_style",  (int)(style ?? BorderStyle));
        return new ColorRect
        {
            Position    = position,
            Size        = size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material    = mat,
        };
    }

    private ShaderMaterial MakeBorderMaterial(Rect2 innerRect)
    {
        var mat = new ShaderMaterial
        {
            Shader = ResourceLoader.Load<Shader>("res://shaders/cel_border_shader.gdshader")
        };
        mat.SetShaderParameter("inner_rect",    new Vector4(innerRect.Position.X, innerRect.Position.Y, innerRect.Size.X, innerRect.Size.Y));
        mat.SetShaderParameter("corner_radius", CornerRadius);
        mat.SetShaderParameter("line_width",    LineWidth);
        mat.SetShaderParameter("softness",      Softness);
        mat.SetShaderParameter("border_style",  (int)BorderStyle);
        return mat;
    }
}
