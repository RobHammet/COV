// page_turn.gdshader — comic book page-curl transition.
//
// A cylinder rolls right-to-left across the page.  As `progress` goes 0→1:
//   • Left of shadow zone: flat unturned page.
//   • Shadow zone:  darkens toward the roll (cubic falloff).
//   • Curl band:    white underside of the page with cylindrical shading.
//   • Past curl tip: transparent — new scene beneath shows through.

shader_type canvas_item;

uniform float progress : hint_range(0.0, 1.0) = 0.0;

const float CURL_BAND = 0.20;   // back-of-page strip width (wide = dramatic roll)
const float SHADOW_W  = 0.10;   // shadow width on unturned page
const float DIAGONAL  = 0.30;   // corner-peel tilt (top leads bottom)

void fragment() {
    vec2 uv = UV;

    // Fold tangent sweeps right → left.
    float fold_x = mix(1.0, -CURL_BAND, progress);

    // Diagonal tilt: top of page leads, peak tilt at mid-progress.
    float tilt = (1.0 - uv.y) * DIAGONAL * sin(progress * PI);
    float fx = fold_x + tilt;

    float d = uv.x - fx;   // <0 = unturned side,  >0 = in or past curl

    if (d < -SHADOW_W) {
        COLOR = texture(TEXTURE, uv);

    } else if (d < 0.0) {
        COLOR = texture(TEXTURE, uv);
        float t = 1.0 - (-d / SHADOW_W);          // 0 far from fold → 1 at fold
        float shadow = t * t * t * 0.75;           // cubic: concentrated right at the crease
        COLOR.rgb *= 1.0 - shadow;

    } else if (d < CURL_BAND) {
        float t = d / CURL_BAND;                   // 0 at fold → 1 at curl tip

        // Off-white paper back; faint ink ghost (as if seen through thin paper).
        vec2 ghost_uv = vec2(clamp(fx - d * 0.4, 0.0, 1.0), uv.y);
        float gray = dot(texture(TEXTURE, ghost_uv).rgb, vec3(0.299, 0.587, 0.114));
        vec3 paper = mix(vec3(0.97, 0.95, 0.91), vec3(gray * 0.6 + 0.3), 0.07);

        // Cylindrical shading: peak brightness at ~30% across the band (top of roll),
        // fading toward both edges (cos envelope).
        float cyl = cos((t - 0.3) * PI * 0.9);
        float lit  = 0.35 + 0.65 * max(0.0, cyl);
        paper *= lit;

        // Sharp crease highlight right at the fold line.
        paper += vec3(exp(-t * 20.0) * 0.50);
        paper  = min(paper, vec3(1.0));

        COLOR = vec4(paper, 1.0);

    } else {
        COLOR = vec4(0.0);
    }
}
