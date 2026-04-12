shader_type canvas_item;

uniform sampler2D sprite_texture;
uniform sampler2D normal_map;

uniform bool enable_shadows  = true;

// ── Shadow / hatch ────────────────────────────────────────────────────────────
uniform float dark_threshold    : hint_range(0.0, 1.0) = 0.15;
uniform float hatch_threshold   : hint_range(0.0, 1.0) = 0.35;
uniform float hatch_spacing     : hint_range(0.1, 64.0) = 1.0;
uniform float hatch_thickness   : hint_range(0.0, 2.0)  = 1.8;
uniform float hatch_roughness   : hint_range(0.0, 1.0)  = 0.05;
uniform float hatch_taper       : hint_range(0.5, 8.0)  = 0.5;
uniform vec2  light_pos_uv      = vec2(0.5, -2.0);
uniform int   hframes           = 1;
uniform int   vframes           = 1;
uniform float light_strength    : hint_range(0, 1);
uniform vec3  light_direction;
uniform bool  is_flipped        = false;
uniform float overall_dark      = 1.0;
uniform float maintain_light    = 0.0;

// ── Hatch helpers ─────────────────────────────────────────────────────────────
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
float radial_line(vec2 pixel_px, vec2 light_px, float scale, float hw,
                  float roughness, float ref_r) {
    vec2  to_light = light_px - pixel_px;
    float r        = length(to_light);
    if (r < 0.5) return 0.0;
    float theta      = atan(to_light.y, to_light.x);
    float angle_step = scale / max(ref_r, 1.0);
    float warp_freq  = 0.008 + roughness * 0.035;
    float wobble_amp = roughness * angle_step * 4.5;
    float warp       = (vnoise(vec2(r * warp_freq, 0.37)) * 2.0 - 1.0) * wobble_amp;
    float h          = fract((theta + warp) / angle_step);
    float arc_dist   = abs(h - 0.5) * angle_step * r;
    float noise_p    = vnoise(vec2(r * 0.028, 0.5));
    float pressure   = mix(1.0, 0.3 + 0.85 * sqrt(noise_p), roughness);
    float edge       = hw * pressure;
    float coverage   = 1.0 - smoothstep(edge * 0.35, edge, arc_dist);
    float break_noise = vnoise(vec2(r * 0.09 + 3.1, theta * 10.0 + 0.8));
    float break_t     = roughness * roughness * 0.65;
    float continuity  = smoothstep(break_t - 0.12, break_t + 0.18, break_noise);
    return coverage * continuity;
}

void fragment() {
    vec4 sprite_color = texture(sprite_texture, UV);

    vec3 final_color;

    if (enable_shadows) {
        vec2 frame_size_px = vec2(textureSize(sprite_texture, 0))
                           / vec2(float(hframes), float(vframes));
        vec2 uv_frame = vec2(fract(UV.x * float(hframes)), fract(UV.y * float(vframes)));

        vec3 normal = normalize((texture(normal_map, UV).rgb * 2.0) - 1.0);

        vec2  light2d   = is_flipped
            ? vec2(-light_direction.x, light_direction.y)
            : light_direction.xy;
        float l2d_len   = length(light2d);
        vec2  light2d_n = l2d_len > 0.001 ? light2d / l2d_len : vec2(1.0, 0.0);

        float shadow_metric = clamp(dot(normal.xy, light2d_n) * 0.5 + 0.5, 0.0, 1.0);

        if (shadow_metric < dark_threshold) {
            final_color = vec3(0.0);

        } else if (shadow_metric < hatch_threshold) {
            float t = (shadow_metric - dark_threshold)
                    / max(hatch_threshold - dark_threshold, 0.001);

            vec2 uv_for_radial = is_flipped
                ? vec2(1.0 - uv_frame.x, uv_frame.y)
                : uv_frame;

            vec2 pixel_px = uv_for_radial * frame_size_px;
            vec2 light_px = light_pos_uv  * frame_size_px;

            vec2  frame_center     = frame_size_px * 0.5;
            vec2  dir_to_light     = light_px - frame_center;
            float actual_dist      = length(dir_to_light);
            float max_virtual_dist = frame_size_px.x * 2.0;
            if (actual_dist > max_virtual_dist)
                light_px = frame_center + normalize(dir_to_light) * max_virtual_dist;

            float ref_r    = frame_size_px.x * 0.5;
            float t_curved = pow(t, hatch_taper);
            float hw       = mix(hatch_spacing * hatch_thickness,
                                 hatch_spacing * hatch_thickness * 0.06,
                                 t_curved);
            float ink      = radial_line(pixel_px, light_px, hatch_spacing, hw, hatch_roughness, ref_r);
            final_color    = ink >= 0.5 ? vec3(0.0) : sprite_color.rgb;

        } else {
            final_color = sprite_color.rgb;
        }
    } else {
        final_color = sprite_color.rgb;
    }

    // ── Scene/ambient darkness (ink pixels stay black) ────────────────────────
    if (final_color != vec3(0.0)) {
        float effective_dark = clamp(overall_dark + maintain_light, 0.0, 1.0);
        final_color *= effective_dark;
    }

    COLOR = vec4(final_color, sprite_color.a);
}
