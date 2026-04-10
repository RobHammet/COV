shader_type canvas_item;

uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;

uniform float noise_scale      : hint_range(1.0, 10.0)  = 4.0;
uniform float vignette_strength: hint_range(0.1, 2.0)   = 0.7;
uniform float sepia_strength   : hint_range(0.0, 1.0)   = 0.85;
uniform float contrast         : hint_range(0.5, 2.0)   = 1.2;

uniform float glow_range       : hint_range(1.0, 30.0)  = 12.0;   // ink bleed radius (pixels)
uniform float glow_strength    : hint_range(0.0, 1.0)   = 0.75;   // bleed intensity
uniform float glow_falloff     : hint_range(0.5, 4.0)   = 2.0;    // falloff curve
uniform float dirt_strength    : hint_range(0.0, 1.0)   = 0.4;
uniform float dirt_scale       : hint_range(1.0, 100.0) = 8.0;

float rand(vec2 co) {
    return fract(sin(dot(co, vec2(12.9898, 78.233))) * 43758.5453);
}
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(rand(i),           rand(i + vec2(1, 0)), f.x),
               mix(rand(i + vec2(0, 1)), rand(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) {
    return noise(p) * 0.5 + noise(p * 2.1) * 0.25 + noise(p * 4.3) * 0.125 + noise(p * 8.7) * 0.063;
}

void fragment() {
    vec3 col = texture(SCREEN_TEXTURE, SCREEN_UV).rgb;

    // Ink bleed: find nearby dark (line/dot) pixels and bleed their colour inward.
    float closest = 1.0;
    vec3 nearestLineColor = vec3(0.0);
    float ga = 2.399963;
    for (int i = 0; i < 32; i++) {
        float r = sqrt(float(i + 1) / 32.0);
        float angle = float(i) * ga;
        float nOffset = fbm(SCREEN_UV * 10.0 + vec2(float(i) * 0.3)) * 1.5;
        vec2 offset = vec2(cos(angle + nOffset), sin(angle + nOffset)) * r * SCREEN_PIXEL_SIZE * glow_range;
        vec4 s = texture(SCREEN_TEXTURE, SCREEN_UV + offset);
        float b = dot(s.rgb, vec3(0.333));
        if (b < 0.45 && r < closest) {
            closest = r;
            nearestLineColor = s.rgb;
        }
    }

    float glowAmount = pow(1.0 - closest, glow_falloff) * glow_strength;
    glowAmount *= (0.6 + fbm(SCREEN_UV * 15.0 + vec2(2.3, 5.1)) * 0.8);
    glowAmount  = clamp(glowAmount, 0.0, 1.0);
    col = mix(col, nearestLineColor, glowAmount);

    // Dirt spots — three fbm layers multiplied so only overlapping dark regions show.
    float dirt = fbm(SCREEN_UV * dirt_scale)
               * fbm(SCREEN_UV * dirt_scale * 2.3 + vec2(4.1, 2.7))
               * fbm(SCREEN_UV * dirt_scale * 0.5 + vec2(1.2, 8.3));
    col -= pow(dirt, 1.5) * dirt_strength;

    // Contrast.
    col = (col - 0.5) * contrast + 0.5;

    // Sepia.
    float gray = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(col, vec3(gray) * vec3(1.12, 0.95, 0.70), sepia_strength);

    // Parchment noise.
    col += (fbm(SCREEN_UV * noise_scale)                          - 0.5) * 0.05;
    col += (fbm(SCREEN_UV * noise_scale * 2.5 + vec2(3.7, 1.9))  - 0.5) * 0.025;

    // Subtle horizontal paper grain.
    col += sin(SCREEN_UV.y * 500.0) * 0.015 + sin(SCREEN_UV.y * 180.0) * 0.007;

    // Vignette.
    vec2 e = SCREEN_UV * (1.0 - SCREEN_UV);
    col *= pow(clamp(e.x * e.y * 16.0, 0.0, 1.0), vignette_strength);

    COLOR = vec4(col, 1.0);
}
