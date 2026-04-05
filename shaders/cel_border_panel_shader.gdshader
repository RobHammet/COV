shader_type canvas_item;

// All values in pixels at the panel's native size.
// Works correctly regardless of screen position or scale during animation.
uniform vec2  panel_size    = vec2(384.0, 216.0);
uniform float border_width  : hint_range(0.0, 100.0) = 24.0;
uniform float line_width    : hint_range(0.0, 20.0)  = 2.0;
uniform float corner_radius : hint_range(0.0, 100.0) = 16.0;
uniform float softness      : hint_range(0.0, 20.0)  = 4.0;

uniform vec4 border_color   : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 line_color     : source_color = vec4(0.0, 0.0, 0.0, 1.0);

float sd_rounded_rect(vec2 p, vec2 b, float r) {
    vec2 q = abs(p) - b + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void fragment() {
    // Pixel coords centred at (0,0) regardless of screen position.
    vec2  p          = (UV - 0.5) * panel_size;
    vec2  inner_half = panel_size * 0.5 - vec2(border_width);
    float r          = max(corner_radius - border_width, 0.0);
    float d          = sd_rounded_rect(p, inner_half, r);

    if (d < -softness) {
        // Centre — fully transparent, thumbnail shows through.
        COLOR = vec4(0.0);
    } else if (d < 0.0) {
        float t = 1.0 - (-d / softness);
        COLOR   = vec4(line_color.rgb, t * line_color.a);
    } else if (d < line_width) {
        COLOR = line_color;
    } else if (d < line_width + softness) {
        float t = (d - line_width) / softness;
        COLOR   = mix(line_color, border_color, t);
    } else {
        COLOR = border_color;
    }
}
