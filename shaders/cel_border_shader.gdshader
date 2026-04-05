shader_type canvas_item;

// inner_rect — x, y, width, height of the transparent window in screen pixels.
// Set from OverlayScene._Ready() by reading the InnerWindow Control node's rect.
uniform vec4  inner_rect    = vec4(40.0, 40.0, 1072.0, 568.0);

// corner_radius — rounded corner radius in pixels (0 = square corners).
// softness      — feather width in pixels at the transparent inner edge.
// line_width    — thickness of the black inner-edge line in pixels.
uniform float corner_radius : hint_range(0.0, 200.0) = 48.0;
uniform float softness      : hint_range(0.0, 40.0)  = 8.0;
uniform float line_width    : hint_range(0.0, 20.0)  = 4.0;

uniform vec4 border_color   : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 line_color     : source_color = vec4(0.0, 0.0, 0.0, 1.0);

// SDF for a rounded rectangle centred at origin, half-extents b, radius r.
float sd_rounded_rect(vec2 p, vec2 b, float r) {
    vec2 q = abs(p) - b + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void fragment() {
    vec2  inner_center = inner_rect.xy + inner_rect.zw * 0.5;
    vec2  inner_half   = inner_rect.zw * 0.5;
    float r            = corner_radius;

    float d = sd_rounded_rect(FRAGCOORD.xy - inner_center, inner_half, r);

    // d < 0: inside the window (transparent)
    // d >= 0: outside (border)

    if (d < -softness) {
        // Well inside — fully transparent.
        COLOR = vec4(0.0);
    } else if (d < 0.0) {
        // Inner soft fade: transparent → black line.
        float t = 1.0 - (-d / softness);
        COLOR   = vec4(line_color.rgb, t * line_color.a);
    } else if (d < line_width) {
        // Solid black line band.
        COLOR = line_color;
    } else if (d < line_width + softness) {
        // Outer soft fade: black line → white border.
        float t = (d - line_width) / softness;
        COLOR   = mix(line_color, border_color, t);
    } else {
        // White border fill.
        COLOR = border_color;
    }
}
