// DarkmapZone.cs — a polygon that defines an area of ambient darkness in a scene.
// Add one or more as children anywhere under the scene node.
// Polygons are drawn as semi-transparent dark overlays in the editor;
// they are hidden at runtime and sampled via point-in-polygon tests.
//
// Darkness: 0.0 = fully lit (no effect), 1.0 = fully shadowed.
// Falloff:  inward distance in pixels over which darkness ramps from 0 to full.
//           0 = hard edge. Overlapping zones: the darkest contribution wins.

using Godot;

[Tool]
public partial class DarkmapZone : Polygon2D
{
    private float _darkness = 0f;

    [Export(PropertyHint.Range, "0,1")]
    public float Darkness
    {
        get => _darkness;
        set { _darkness = value; Color = new Color(0f, 0f, 0f, _darkness * 0.55f); }
    }

    [Export(PropertyHint.Range, "0,300")]
    public float Falloff = 0f;

    public override void _Ready()
    {
        Color = new Color(0f, 0f, 0f, _darkness * 0.55f);
        if (!Engine.IsEditorHint())
            Visible = false;
    }
}
