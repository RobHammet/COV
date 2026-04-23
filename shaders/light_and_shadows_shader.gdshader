shader_type canvas_item;

uniform sampler2D sprite_texture;
uniform sampler2D normal_map;

uniform bool enable_shadows = true;

// shadow_metric = 0 at the far edge from the light, 1 at fully lit.
// Pixels with shadow_metric < shadow_threshold are in the shadow zone.
uniform float shadow_threshold  : hint_range(0.0, 1.0) = 0.35;

// hw_ratio at shadow core = hatch_thickness * pow(1, hatch_taper).
// hw_ratio >= 1.0 means strokes physically touch; > ~2.5 guarantees solid fill.
uniform float hatch_thickness   : hint_range(0.1, 4.0)  = 2.0;

// Angular spacing between strokes (pixels at reference radius).
uniform float hatch_spacing     : hint_range(0.1, 64.0) = 1.0;

// Density falloff: low = hatches compressed near lit boundary; high = hatches spread across full shadow zone.
uniform float hatch_taper       : hint_range(0.2, 4.0)  = 1.5;

// Organic roughness: wobble amplitude and continuity breaks.
uniform float hatch_roughness   : hint_range(0.0, 1.0)  = 0.06;

// Strokes thinner than this hw_ratio are suppressed entirely.
uniform float hatch_min_ratio   : hint_range(0.0, 2.0)  = 0.3;

// Noise wash strength: ink pooling effect that blends hatches in deep shadow.
uniform float hatch_noise_strength : hint_range(0.0, 1.0) = 0.4;

uniform vec2  light_pos_uv    = vec2(0.5, -2.0);
uniform int   hframes         = 1;
uniform int   vframes         = 1;
uniform float light_strength  : hint_range(0, 1) = 1.0;
uniform vec3  light_direction;
uniform bool  is_flipped      = false;
uniform float overall_dark    = 1.0;
uniform float maintain_light  = 0.0;

// ── Noise ─────────────────────────────────────────────────────────────────────

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
        f.y);
}

// ── Pen-stroke radial hatch ───────────────────────────────────────────────────
//
// hw_ratio is the stroke half-width as a multiple of the local half-period:
//   hw_ratio = 0   → no ink
//   hw_ratio = 1.0 → strokes just touching (still some gaps due to per-stroke variation)
//   hw_ratio > 1.6 → solid black guaranteed via fill fallback
//
// Stroke character: two-stroke coverage (fills gaps when dense), per-stroke weight
// variation, pressure oscillation, near-taper, asymmetric nib profile.
//
float radial_line(vec2 pixel_px, vec2 light_px, float scale, float hw_ratio,
                  float roughness, float ref_r, float phase) {
    vec2  to_light = light_px - pixel_px;
    float r        = length(to_light);
    if (r < 0.5) return 0.0;

    float theta      = atan(to_light.y, to_light.x);
    float angle_step = scale / max(ref_r, 1.0);   // radians per stroke period
    float period     = angle_step * r;             // px between adjacent stroke centres
    float half_period = period * 0.5;

    // hw in pixels — scales with r so density is consistent at any light distance
    float hw_px = hw_ratio * half_period;

    // Organic path wobble
    float warp_freq  = 0.008 + roughness * 0.035;
    float wobble_amp = roughness * angle_step * 4.5;
    float warp       = (vnoise(vec2(r * warp_freq, 0.37)) * 2.0 - 1.0) * wobble_amp;

    float norm_pos   = (theta + warp) / angle_step + phase;
    float h_frac     = fract(norm_pos);          // [0,1), stroke centre at h=0.5
    float stroke_idx = floor(norm_pos);

    // Pixel-to-stroke distances (two nearest neighbours)
    float arc_A = abs(h_frac - 0.5) * period;   // distance to stroke_idx's centre
    float arc_B = period - arc_A;               // distance to the other neighbour

    // Seeds for both neighbouring strokes
    float sign_side = sign(h_frac - 0.5 + 0.0001);
    float seed_A    = hash21(vec2(stroke_idx,            17.3));
    float seed_B    = hash21(vec2(stroke_idx + sign_side, 17.3));

    // ── Per-stroke weight variation ───────────────────────────────────────────
    float var_A  = 0.75 + 0.50 * seed_A;
    float var_B  = 0.75 + 0.50 * seed_B;

    // ── Pressure oscillation along stroke (r direction) ───────────────────────
    float press_A = 0.65 + 0.35 * sin(r * (0.030 + seed_A * 0.028) + seed_A * 6.2832);
    float press_B = 0.65 + 0.35 * sin(r * (0.030 + seed_B * 0.028) + seed_B * 6.2832);

    // ── Near-taper: strokes thin toward the light source ─────────────────────
    float near_taper = smoothstep(0.0, ref_r * 0.25, r);

    // ── Asymmetric nib profile (one edge crisper, one softer) ─────────────────
    float nib_A  = mix(-0.22, 0.22, seed_A);
    float nib_B  = mix(-0.22, 0.22, seed_B);
    float asym_A = 1.0 + sign_side        * nib_A;
    float asym_B = 1.0 + (-sign_side)     * nib_B;  // pixel approaches B from the opposite side

    // ── Coverage from each neighbour ──────────────────────────────────────────
    float edge_A = hw_px * var_A * press_A * near_taper * asym_A;
    float edge_B = hw_px * var_B * press_B * near_taper * asym_B;

    float cov_A  = 1.0 - smoothstep(edge_A * 0.25, edge_A, arc_A);
    float cov_B  = 1.0 - smoothstep(edge_B * 0.25, edge_B, arc_B);

    float coverage = max(cov_A, cov_B);

    // ── Solid-fill guarantee when strokes fully overlap ───────────────────────
    // Reaches 1 at hw_ratio ≈ 2.5 — only the deepest shadow becomes solid black.
    // Keep this threshold high so hatches remain visible across the shadow zone.
    float fill = smoothstep(1.5, 2.5, hw_ratio);
    coverage   = max(coverage, fill);

    // ── Continuity breaks (roughness-driven gaps) ─────────────────────────────
    float break_noise = vnoise(vec2(r * 0.09 + 3.1, theta * 10.0 + 0.8));
    float break_t     = roughness * roughness * 0.65;
    float continuity  = smoothstep(break_t - 0.12, break_t + 0.18, break_noise);

    // Don't break strokes inside the guaranteed-solid zone
    return mix(coverage * continuity, coverage, fill);
}

// ── Hatch-light snapping ─────────────────────────────────────────────────────
// Returns a virtual light position snapped to the nearest 30° step so radial
// hatch lines are never near-vertical. Distance is also snapped to three
// discrete levels for consistent shadow depth variety.
// When the real light is directly above/below (±90°), the nearer step with a
// more-negative x (leftward) is chosen.
vec2 snap_hatch_light(vec2 raw_light_px, vec2 center, float W) {
    const float PI6 = 0.5235987756;  // π/6 — 30° step

    vec2  d    = raw_light_px - center;
    float dist = length(d);
    if (dist < 1.0) return center + vec2(-W, 0.0);  // degenerate: default left

    float snap_dist = dist < W * 1.25 ? W
                    : dist < W * 1.75 ? W * 1.5
                    :                   W * 2.0;

    float angle   = atan(d.y, d.x);
    float snapped = round(angle / PI6) * PI6;

    // Nudge away from vertical: if |cos| < 0.26 the angle is within ~75° of vertical
    if (abs(cos(snapped)) < 0.26) {
        float a1 = snapped - PI6;
        float a2 = snapped + PI6;
        // Prefer the more-leftward candidate (smaller cosine = more negative x)
        snapped = cos(a1) < cos(a2) ? a1 : a2;
    }

    return center + vec2(cos(snapped), sin(snapped)) * snap_dist;
}

// ── Fragment ──────────────────────────────────────────────────────────────────

void fragment() {
    vec4 sprite_color = texture(sprite_texture, UV);
    vec3 final_color  = sprite_color.rgb;

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

        if (shadow_metric < shadow_threshold) {
            // t = 1 at darkest, 0 at shadow/lit boundary.
            float t        = 1.0 - shadow_metric / max(shadow_threshold, 0.001);
            float hw_ratio = hatch_thickness * pow(t, hatch_taper);

            if (hw_ratio < hatch_min_ratio) { /* too thin — leave sprite colour */ }
            else {

            vec2 uv_for_radial = is_flipped
                ? vec2(1.0 - uv_frame.x, uv_frame.y)
                : uv_frame;

            vec2  pixel_px     = uv_for_radial * frame_size_px;
            vec2  frame_center = frame_size_px * 0.5;
            vec2  light_px     = snap_hatch_light(light_pos_uv * frame_size_px,
                                                  frame_center, frame_size_px.x);

            float ref_r = frame_size_px.x * 0.5;
            float ink1  = radial_line(pixel_px, light_px, hatch_spacing, hw_ratio,
                                      hatch_roughness, ref_r, 0.0);

            // Cross-hatch layer: offset by half period to fill gaps, fades in over dark half
            float cross_weight = smoothstep(0.3, 0.7, t);
            float ink2  = radial_line(pixel_px, light_px, hatch_spacing, hw_ratio * 0.7,
                                      hatch_roughness, ref_r, 0.5);
            float ink   = 1.0 - (1.0 - ink1) * (1.0 - ink2 * cross_weight);

            // Noise wash: multi-octave pooling fades in over deep shadow
            if (hatch_noise_strength > 0.0) {
                float noise_weight = smoothstep(0.4, 0.9, t) * hatch_noise_strength;
                float nv = vnoise(pixel_px * 0.035) * 0.55
                         + vnoise(pixel_px * 0.080) * 0.30
                         + vnoise(pixel_px * 0.180) * 0.15;
                float noise_ink = smoothstep(0.38, 0.62, nv) * noise_weight;
                ink = 1.0 - (1.0 - ink) * (1.0 - noise_ink);
            }

            final_color = mix(sprite_color.rgb, vec3(0.0), ink);
            } // end hw_ratio >= hatch_min_ratio
        }
    }

    // Scene/ambient darkness — ink pixels stay black
    if (final_color != vec3(0.0)) {
        float effective_dark = clamp(overall_dark + maintain_light, 0.0, 1.0);
        final_color *= effective_dark;
    }

    COLOR = vec4(final_color, sprite_color.a);
}
