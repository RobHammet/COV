// page_turn.gdshader — comic book page-curl transition.
//
// Applied to a full-screen TextureRect holding a snapshot of the outgoing scene.
// As `progress` goes 0→1, a fold line sweeps right-to-left.  Pixels left of the
// fold render normally (with a shadow cast by the curl); pixels in the curl band
// show the darkened back-of-page; pixels past the curl are transparent so the new
// scene beneath shows through.
//
// Tunable constants (edit here or expose as uniforms if you want runtime control):
//   CURL_BAND   — width of the visible back-of-page strip in UV space (0–1)
//   SHADOW_W    — softness of the shadow cast onto the unturned page
//   DIAGONAL    — how much the fold tilts top-before-bottom (0 = straight vertical)
//   BACK_DARK   — how dark the back-of-page strip appears (0 = same, 1 = black)

shader_type canvas_item;

uniform float progress : hint_range(0.0, 1.0) = 0.0;

const float CURL_BAND = 0.07;   // back-of-page strip width
const float SHADOW_W  = 0.055;  // shadow gradient width
const float DIAGONAL  = 0.22;   // diagonal tilt of fold (top turns first)
const float BACK_DARK = 0.62;   // darkness of back-of-page

void fragment() {
    vec2 uv = UV;

    // Fold center sweeps from x=1.0 (right edge) to x=-CURL_BAND (past left edge).
    // Adding CURL_BAND to the end position ensures the strip fully exits left.
    float fold_x = mix(1.0, -CURL_BAND, progress);

    // Tilt: top of the page leads the bottom (a natural top-right corner peel).
    // The offset grows with progress so the tilt is zero at the start and maximum
    // at the midpoint of the sweep, then decreases symmetrically.
    float tilt = (1.0 - uv.y) * DIAGONAL * sin(progress * PI);
    float fx = fold_x + tilt;

    float d = uv.x - fx;  // negative = left of fold (unturned), positive = right (curled)

    if (d < -SHADOW_W) {
        // ── Unturned page, no shadow ──────────────────────────────────────────
        COLOR = texture(TEXTURE, uv);

    } else if (d < 0.0) {
        // ── Unturned page, in shadow cast by the curl ─────────────────────────
        COLOR = texture(TEXTURE, uv);
        // Shadow fades smoothly from the fold line outward.
        float shadow_t = 1.0 - (-d / SHADOW_W);   // 1 at fold, 0 at SHADOW_W
        COLOR.rgb *= 1.0 - shadow_t * 0.5;

    } else if (d < CURL_BAND) {
        // ── Visible back of the curled page ───────────────────────────────────
        // The back UV is the reflection of this pixel across the fold line.
        float t = d / CURL_BAND;            // 0 at fold edge, 1 at curl tip
        vec2 back_uv = vec2(fx - d, uv.y);
        back_uv.x = clamp(back_uv.x, 0.0, 1.0);

        vec4 src = texture(TEXTURE, back_uv);

        // Desaturate slightly and darken; darker toward the curling tip.
        float gray = dot(src.rgb, vec3(0.299, 0.587, 0.114));
        vec3 desaturated = mix(src.rgb, vec3(gray), 0.45);
        float darkness = BACK_DARK * (0.4 + t * 0.6);   // gradient across the strip
        COLOR = vec4(desaturated * (1.0 - darkness), src.a);

        // Bright highlight line right at the fold crease.
        float highlight = exp(-t * 18.0) * 0.35;
        COLOR.rgb = min(COLOR.rgb + vec3(highlight), vec3(1.0));

    } else {
        // ── Past the curl tip: transparent ────────────────────────────────────
        // The TextureRect alpha lets the new scene show through here.
        COLOR = vec4(0.0);
    }
}
