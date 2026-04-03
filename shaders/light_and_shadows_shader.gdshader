shader_type canvas_item;

uniform sampler2D sprite_texture;
uniform sampler2D normal_map;
uniform float dark_threshold;
uniform float hatch_threshold : hint_range(0.0, 1.0) = 0.0;
uniform float hatch_scale     : hint_range(2.0, 32.0) = 6.0;
uniform float hatch_wobble    : hint_range(0.0, 16.0) = 5.0;
uniform float light_strength  : hint_range(0, 1);
uniform vec3  light_direction;
uniform bool  is_flipped     = false;
uniform float overall_dark   = 1.0;
uniform float maintain_light = 0.0;

// -------------------------------------------------------------------------
// Smooth value noise.
// -------------------------------------------------------------------------
float hash21(vec2 p) {
    p = fract(p * vec2(127.1, 311.7));
    p += dot(p, p + 19.19);
    return fract(p.x * p.y);
}

float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = smoothstep(0.0, 1.0, fract(p));
    return mix(
        mix(hash21(i),                  hash21(i + vec2(1.0, 0.0)), f.x),
        mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x),
        f.y
    );
}

// -------------------------------------------------------------------------
// Single pen-line family test.
//
// uv_px    — UV coords scaled to sprite pixel dimensions (stable, surface-locked).
// line_dir — unit vector along the line; lines run parallel to this.
// hw       — base half-width as a fraction of hatch_scale [0..0.5].
// wobble   — amplitude of organic warp in pixels.
// seed     — per-family offset so families curve independently.
//
// Lines sit at h = 0.5 in each mod cell. Width varies along the line
// via sqrt-biased noise (mostly thick, tapering to thin at random points).
// -------------------------------------------------------------------------
bool pen_line(vec2 uv_px, vec2 line_dir, float scale, float hw, float wobble, float seed) {
    vec2 perp   = vec2(-line_dir.y, line_dir.x);
    float across = dot(uv_px, perp);     // perpendicular — determines which line
    float along  = dot(uv_px, line_dir); // parallel       — position along the line

    // Warp the cross-line coordinate using noise keyed to position along the line.
    // This curves the line without disturbing the spacing between lines.
    float warp = (vnoise(vec2(along * 0.018 + seed, seed * 0.4)) * 2.0 - 1.0) * wobble;

    // Distance from line centre within each cell, [0 .. 0.5].
    float h    = fract((across + warp) / scale);
    float dist = abs(h - 0.5);

    // Width varies along the line: sqrt bias keeps it mostly thick with
    // occasional narrow passages that read as natural tapering.
    float width_var = sqrt(vnoise(vec2(along * 0.028 + seed * 1.3, seed * 0.6)));

    return dist < hw * width_var;
}

void fragment() {
    vec2 uv = UV;

    vec4 sprite_color = texture(sprite_texture, uv);
    vec3 normal       = (texture(normal_map, uv).rgb * 2.0) - 1.0;
    vec3 interp_n     = normalize(normal);

    vec3 light_dir2 = light_direction;
    if (is_flipped) light_dir2.x = -(light_direction.x);
    light_dir2.z = clamp((light_strength * 30.0) - 15.0, -15.0, 15.0);

    float ndotl      = max(dot(interp_n, light_dir2), 0.0);
    vec3  lit_sample = sprite_color.rgb * ndotl * light_strength;
    float brightness = (lit_sample.r + lit_sample.g + lit_sample.b) / 3.0;

    vec3 final_color;

    if (brightness < dark_threshold) {
        final_color = vec3(0.0);

    } else if (hatch_threshold > dark_threshold && brightness < hatch_threshold) {
        // t = 0 at shadow edge (thick/dense), t = 1 at light edge (thin/sparse).
        float t = (brightness - dark_threshold) / (hatch_threshold - dark_threshold);

        // Primary line direction from surface tangent; falls back to 45°.
        vec2  n2d  = normal.xy;
        float nlen = length(n2d);
        vec2  dir1 = nlen > 0.01
            ? normalize(vec2(-n2d.y, n2d.x))
            : normalize(vec2(1.0, 1.0));

        // Second family at 65° — oblique crossing, no right angles.
        float ang  = 1.134;
        vec2  dir2 = vec2(
            dir1.x * cos(ang) - dir1.y * sin(ang),
            dir1.x * sin(ang) + dir1.y * cos(ang)
        );

        // UV in texture-pixel units — locked to sprite surface, camera-independent.
        vec2 uv_px = uv * vec2(textureSize(sprite_texture, 0));

        // Half-width: thick at shadow edge, thinner toward light edge.
        float hw = mix(0.42, 0.07, t);

        bool line1 = pen_line(uv_px, dir1, hatch_scale, hw,        hatch_wobble, 0.0);
        bool line2 = t < 0.5
                  && pen_line(uv_px, dir2, hatch_scale, hw * 0.75, hatch_wobble, 13.7);

        final_color = (line1 || line2) ? vec3(0.0) : sprite_color.rgb;

    } else {
        final_color = sprite_color.rgb;
    }

    if (final_color != vec3(0.0)) {
        float new_overall_dark = clamp(overall_dark + maintain_light, 0.0, 1.0);
        final_color *= new_overall_dark;
    }

    COLOR = vec4(final_color, sprite_color.a);
}
