// ScalerStick.cs — editor-visible scale reference for scene depth zones.
//
// Attach this script to the "ScalerStick" Line2D in any room scene.
// Set the Line2D's DefaultColor to transparent in the editor so the node is
// visible (enabling _Draw) but the built-in line rendering is invisible.
//
// The three points define Y positions where ego scale = 0.25 / 1 / 2.
// Set EgoHeight to the ego sprite's pixel height at scale = 1 (the mid-point).
// _Draw redraws the connecting line and silhouette bars at each reference point.

using Godot;

[Tool]
public partial class ScalerStick : Line2D
{
    [Export] public float EgoHeight       = 190f;
    [Export] public float ScaleMultiplier = 1f;

    private static readonly Color _lineColor = new(0.4f, 0.8f, 1f, 0.7f);
    private static readonly Color _barColor  = new(1f, 0.85f, 0f, 0.45f);
    private static readonly Color _baseColor = new(1f, 0.85f, 0f, 0.9f);
    private const float _barHalfWidth = 12f;

    // Scale values represented by Points[0], [1], [2].
    private static readonly float[] _scales = [0.25f, 1f, 2f];

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Engine.IsEditorHint() || Points.Length < 3)
            return;

        // Redraw the connecting line (DefaultColor is transparent, so we own it).
        for (int i = 0; i < Points.Length - 1; i++)
            DrawLine(Points[i], Points[i + 1], _lineColor, 2f);

        if (EgoHeight <= 0f) return;

        for (int i = 0; i < 3; i++)
        {
            float   h    = EgoHeight * _scales[i] * ScaleMultiplier;
            Vector2 foot = Points[i];

            // Horizontal tick mark.
            DrawLine(new Vector2(foot.X - _barHalfWidth * 1.5f, foot.Y),
                     new Vector2(foot.X + _barHalfWidth * 1.5f, foot.Y),
                     _baseColor, 2f);

            // Filled silhouette bar rising upward.
            DrawRect(new Rect2(foot.X - _barHalfWidth, foot.Y - h,
                               _barHalfWidth * 2f, h),
                     _barColor);

            // Outline.
            DrawLine(new Vector2(foot.X - _barHalfWidth, foot.Y),
                     new Vector2(foot.X - _barHalfWidth, foot.Y - h), _baseColor, 1.5f);
            DrawLine(new Vector2(foot.X + _barHalfWidth, foot.Y),
                     new Vector2(foot.X + _barHalfWidth, foot.Y - h), _baseColor, 1.5f);
            DrawLine(new Vector2(foot.X - _barHalfWidth, foot.Y - h),
                     new Vector2(foot.X + _barHalfWidth, foot.Y - h), _baseColor, 1.5f);
        }
    }
}
