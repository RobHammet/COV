// page_turn.gdshader - comic book page-curl transition.
//
// Forward flip: node extends LEFT by overhang; UV.x in [0, left_overhang) is transparent.
// Backward flip: node extends RIGHT by overhang; UV.x in (1-left_overhang, 1] is transparent.
// px   = texture UV x (non-mirrored; maps cover rect to [0,1])
// foldX = px mirrored for backward; used only for fold position, not texture sampling.

shader_type canvas_item;

uniform float progress      : hint_range(0.0, 1.0) = 0.0;
uniform float left_overhang : hint_range(0.0, 0.5) = 0.0;
// 1 = backward (right-to-left curl); node placed at coverX instead of coverX - overhang.
uniform int   backward                             = 0;

const float CURL_BAND = 0.20;
const float SHADOW_W  = 0.10;
const float DIAGONAL  = 0.30;

void fragment() {
    float bk    = float(backward);
    float shift = mix(left_overhang, 0.0, bk);
    float px    = (UV.x - shift) / (1.0 - left_overhang);   // texture UV, never mirrored
    float foldX = mix(px, 1.0 - px, bk);                    // fold position only

    float fold_x = mix(1.0, -CURL_BAND, progress);
    float tilt   = (1.0 - UV.y) * DIAGONAL * sin(progress * PI);
    float fx     = fold_x + tilt;
    float d      = foldX - fx;

    bool  overhang = (bk < 0.5) ? (px < 0.0) : (px > 1.0);
    vec2  uv       = vec2(px, UV.y);

    if (d < -SHADOW_W) {
        COLOR = overhang ? vec4(0.0) : texture(TEXTURE, uv);

    } else if (d < 0.0) {
        if (overhang) {
            COLOR = vec4(0.0);
        } else {
            COLOR        = texture(TEXTURE, uv);
            float t      = 1.0 - (-d / SHADOW_W);
            float shadow = t * t * t * 0.75;
            COLOR.rgb   *= 1.0 - shadow;
        }

    } else if (d < CURL_BAND) {
        float t        = d / CURL_BAND;
        // sample ghost in fold-space, convert back to texture space
        float ghost_t  = fx - d * 0.4;
        float ghost_px = mix(ghost_t, 1.0 - ghost_t, bk);
        vec2  ghost_uv = vec2(clamp(ghost_px, 0.0, 1.0), UV.y);
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
