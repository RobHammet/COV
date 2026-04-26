// page_turn.gdshader - comic book page-curl transition.
//
// left_overhang: fraction of the node width that lies LEFT of the page.
// The node is extended leftward so the curl band can rest outside the page.
// UV.x in [0, left_overhang) is transparent; [left_overhang, 1] is the page.
// Sampling texture(TEXTURE, uv) where uv.x = (UV.x - lo) / (1 - lo) gives
// the correct unscaled page pixel even though the node is wider than the page.

shader_type canvas_item;

uniform float progress      : hint_range(0.0, 1.0) = 0.0;
uniform float left_overhang : hint_range(0.0, 0.5) = 0.0;

const float CURL_BAND = 0.20;
const float SHADOW_W  = 0.10;
const float DIAGONAL  = 0.30;

void fragment() {
    vec2 uv = vec2((UV.x - left_overhang) / (1.0 - left_overhang), UV.y);

    float fold_x = mix(1.0, -CURL_BAND, progress);
    float tilt   = (1.0 - uv.y) * DIAGONAL * sin(progress * PI);
    float fx     = fold_x + tilt;
    float d      = uv.x - fx;

    if (d < -SHADOW_W) {
        if (uv.x < 0.0) {
            COLOR = vec4(0.0);
        } else {
            COLOR = texture(TEXTURE, uv);
        }

    } else if (d < 0.0) {
        if (uv.x < 0.0) {
            COLOR = vec4(0.0);
        } else {
            COLOR = texture(TEXTURE, uv);
            float t      = 1.0 - (-d / SHADOW_W);
            float shadow = t * t * t * 0.75;
            COLOR.rgb   *= 1.0 - shadow;
        }

    } else if (d < CURL_BAND) {
        float t        = d / CURL_BAND;
        vec2  ghost_uv = vec2(clamp(fx - d * 0.4, 0.0, 1.0), uv.y);
        float gray     = dot(texture(TEXTURE, ghost_uv).rgb, vec3(0.299, 0.587, 0.114));
        vec3  paper    = mix(vec3(0.97, 0.95, 0.91), vec3(gray * 0.6 + 0.3), 0.07);
        float cyl      = cos((t - 0.3) * PI * 0.9);
        float lit      = 0.35 + 0.65 * max(0.0, cyl);
        paper         *= lit;
        paper         += vec3(exp(-t * 20.0) * 0.50);
        paper          = min(paper, vec3(1.0));
        COLOR          = vec4(paper, 1.0);

    } else {
        COLOR = vec4(0.0);
    }
}
