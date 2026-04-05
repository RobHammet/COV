shader_type canvas_item;

uniform sampler2D sprite_texture;
uniform sampler2D normal_map;

// dark_threshold  — shadow_metric below this → solid black.
// hatch_threshold — shadow_metric below this (and above dark) → hatch zone.
uniform float dark_threshold    : hint_range(0.0, 1.0) = 0.15;
uniform float hatch_threshold   : hint_range(0.0, 1.0) = 0.55;

// hatch_spacing   — arc-length spacing between lines at half-frame-width from the light.
// hatch_thickness — line width as a fraction of spacing (0=hair-thin, 1=nearly solid).
// hatch_roughness — breaks, roughens and brushifies individual strokes.
// hatch_taper     — how/where lines thin toward the light edge.
//                   low (0.5-1) = gradual taper across the whole zone.
//                   high (4-8)  = lines stay full thickness, thin only at the very tip.
uniform float hatch_spacing     : hint_range(0.1, 64.0) = 8.0;
uniform float hatch_thickness   : hint_range(0.0, 2.0)  = 0.36;
uniform float hatch_roughness   : hint_range(0.0, 1.0)  = 0.5;
uniform float hatch_taper       : hint_range(0.5, 8.0)  = 3.0;

// light_pos_uv — light position in frame UV space [0,1]x[0,1].
//   Set per-frame from scene_script._Process() via SetShaderParameter.
//   Values outside [0,1] are valid (light outside the sprite frame).
// hframes / vframes — spritesheet divisions, matching Sprite2D.Hframes/Vframes.
uniform vec2  light_pos_uv      = vec2(0.5, -2.0);
uniform int   hframes           = 1;
uniform int   vframes           = 1;

// light_direction still drives the shadow metric via normal-map dot product.
uniform float light_strength    : hint_range(0, 1);
uniform vec3  light_direction;
uniform bool  is_flipped        = false;
uniform float overall_dark      = 1.0;
uniform float maintain_light    = 0.0;

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
// radial_line — returns ink coverage [0,1].
//
// Lines radiate from light_px toward the outer edge of the sprite.
// Fixed angular step → lines diverge naturally; spacing grows with distance
// from the light, so they are never parallel.
//
//   pixel_px  — current pixel in frame-space pixels (frame top-left = origin).
//   light_px  — light position in the same frame-space pixels.
//   hw        — line half-width in pixels.
//   roughness — 0=clean ruled rays, 1=broken brushy strokes.
//   ref_r     — reference radius for hatch_spacing (= half frame width).
// -------------------------------------------------------------------------
float radial_line(vec2 pixel_px, vec2 light_px, float scale, float hw,
                  float roughness, float ref_r) {
    vec2  to_light = light_px - pixel_px;
    float r        = length(to_light);
    if (r < 0.5) return 0.0;

    float theta = atan(to_light.y, to_light.x);

    // Fixed angular step: arc spacing at ref_r equals hatch_spacing pixels.
    // Spacing grows linearly with distance → lines diverge from the light.
    float angle_step = scale / max(ref_r, 1.0);

    // Lateral wobble: angular perturbation whose amplitude grows with roughness.
    float warp_freq  = 0.008 + roughness * 0.035;
    float wobble_amp = roughness * angle_step * 4.5;
    float warp = (vnoise(vec2(r * warp_freq, 0.37)) * 2.0 - 1.0) * wobble_amp;

    // Which ray we're on; arc distance to the nearest ray centre.
    float h        = fract((theta + warp) / angle_step);
    float arc_dist = abs(h - 0.5) * angle_step * r;

    // Pressure variation along the ray (radially outward from light).
    float noise_p  = vnoise(vec2(r * 0.028, 0.5));
    float pressure = mix(1.0, 0.3 + 0.85 * sqrt(noise_p), roughness);
    float edge     = hw * pressure;

    float coverage = 1.0 - smoothstep(edge * 0.35, edge, arc_dist);

    // Along-ray breakage: creates gaps / dry-brush skips at high roughness.
    float break_noise = vnoise(vec2(r * 0.09 + 3.1, theta * 10.0 + 0.8));
    float break_t     = roughness * roughness * 0.65;
    float continuity  = smoothstep(break_t - 0.12, break_t + 0.18, break_noise);

    return coverage * continuity;
}

void fragment() {
    vec4 sprite_color = texture(sprite_texture, UV);
    vec3 normal       = normalize((texture(normal_map, UV).rgb * 2.0) - 1.0);

    // Light direction for shadow metric (far-field approximation per-sprite).
    // X is negated when sprite is flipped so the normal-map dot product stays correct.
    vec2  light2d   = is_flipped
        ? vec2(-light_direction.x, light_direction.y)
        : light_direction.xy;
    float l2d_len   = length(light2d);
    vec2  light2d_n = l2d_len > 0.001 ? light2d / l2d_len : vec2(1.0, 0.0);

    // Shadow metric: 0 = fully away from light, 1 = facing light.
    float shadow_metric = clamp(dot(normal.xy, light2d_n) * 0.5 + 0.5, 0.0, 1.0);

    vec3 final_color;

    if (shadow_metric < dark_threshold) {
        // ── Solid shadow ─────────────────────────────────────────────────
        final_color = vec3(0.0);

    } else if (shadow_metric < hatch_threshold) {
        // ── Hatch zone ───────────────────────────────────────────────────
        // t = 0 at the dark edge, 1 at the light (terminator) edge.
        float t = (shadow_metric - dark_threshold)
                / max(hatch_threshold - dark_threshold, 0.001);

        // Frame-relative pixel coordinates.
        // fract(UV * hframes/vframes) gives [0,1) within the current animation
        // frame regardless of which frame is active in the spritesheet.
        vec2 frame_size_px = vec2(textureSize(sprite_texture, 0))
                           / vec2(float(hframes), float(vframes));

        vec2 uv_frame = vec2(
            fract(UV.x * float(hframes)),
            fract(UV.y * float(vframes)));

        // When the sprite is rendered flipped, UV.x runs right-to-left.
        // Un-flip so that pixel_px and light_pos_uv share the same orientation.
        vec2 uv_for_radial = is_flipped
            ? vec2(1.0 - uv_frame.x, uv_frame.y)
            : uv_frame;

        vec2 pixel_px = uv_for_radial * frame_size_px;
        vec2 light_px = light_pos_uv  * frame_size_px;

        // Clamp the virtual light to within max_virtual_dist of the frame centre.
        // This preserves the light direction while keeping line density consistent
        // regardless of how far the actual PointLight2D is from the sprite.
        vec2  frame_center    = frame_size_px * 0.5;
        vec2  dir_to_light    = light_px - frame_center;
        float actual_dist     = length(dir_to_light);
        float max_virtual_dist = frame_size_px.x * 2.0;
        if (actual_dist > max_virtual_dist)
            light_px = frame_center + normalize(dir_to_light) * max_virtual_dist;

        // Reference radius: half the frame width, so hatch_spacing = arc spacing
        // at the sprite's visual edge when the light is centred on the frame.
        float ref_r = frame_size_px.x * 0.5;

        // Half-width: hatch_taper controls where lines start to thin.
        float t_curved = pow(t, hatch_taper);
        float hw = mix(hatch_spacing * hatch_thickness,
                       hatch_spacing * hatch_thickness * 0.06,
                       t_curved);

        float ink = radial_line(pixel_px, light_px, hatch_spacing, hw, hatch_roughness, ref_r);

        // Binary: either black ink or the unmodified sprite colour — no grey.
        // Dense lines at t≈0 fully overlap → solid black, blending seamlessly
        // into the dark shadow zone without a visible seam.
        final_color = ink >= 0.5 ? vec3(0.0) : sprite_color.rgb;

    } else {
        // ── Lit zone ─────────────────────────────────────────────────────
        final_color = sprite_color.rgb;
    }

    // Scene/ambient darkness applied only to unshaded (sprite) pixels.
    if (final_color != vec3(0.0)) {
        float effective_dark = clamp(overall_dark + maintain_light, 0.0, 1.0);
        final_color *= effective_dark;
    }

    COLOR = vec4(final_color, sprite_color.a);
}
