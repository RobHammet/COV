/*
	印刷物風ハーフトーンシェーダー by あるる（きのもと 結衣） @arlez80
	Halftone Shader by Yui Kinomoto
	MIT License

	Extended with paper-texture / ink-bleed pass.
*/
shader_type canvas_item;

uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;

// ── Halftone ──────────────────────────────────────────────────────────────────
uniform float radius_c   = 4.0;
uniform float radius_m   = 5.0;
uniform float radius_y   = 6.0;
uniform float radius_k   = 7.0;
uniform float frequency  = 330.0;
uniform vec2  uv_offset  = vec2(0.0, 0.0);
uniform float halftone_blend     : hint_range(0.0, 1.0) = 0.3;
uniform float halftone_luma_min  : hint_range(0.0, 0.5) = 0.08;
uniform float halftone_luma_max  : hint_range(0.5, 1.0) = 0.92;

// ── Paper texture ─────────────────────────────────────────────────────────────
uniform float noise_scale        : hint_range(1.0, 10.0)  = 2.256;
uniform float vignette_strength  : hint_range(0.1, 2.0)   = 0.1;
uniform float sepia_strength     : hint_range(0.0, 1.0)   = 0.4;
uniform float contrast           : hint_range(0.5, 2.0)   = 1.0;
uniform float glow_range         : hint_range(1.0, 30.0)  = 2.783;
uniform float glow_strength      : hint_range(0.0, 1.0)   = 0.184;
uniform float glow_falloff       : hint_range(0.5, 4.0)   = 2.16;
uniform float dirt_strength      : hint_range(0.0, 1.0)   = 0.559;
uniform float dirt_scale         : hint_range(1.0, 100.0) = 61.721;

// ── Paper UV mapping ──────────────────────────────────────────────────────────
// effectUV = UV * uv_noise_scale + uv_noise_offset.
// Set by code so that effectUV == SCREEN_UV at transition start (no jump),
// then tweened toward UV == SCREEN_UV at full-page scale (wider paper).
// vec2 because the portrait page has different X/Y ratios relative to the viewport.
uniform vec2 uv_noise_scale  = vec2(1.0, 1.0);
uniform vec2 uv_noise_offset = vec2(0.0, 0.0);

// dirtUV = UV * uv_dirt_scale + uv_dirt_offset.
// Set on ComicPageLayer at transition end so dirt aligns with the page container's UV.
uniform vec2 uv_dirt_scale  = vec2(1.0, 1.0);
uniform vec2 uv_dirt_offset = vec2(0.0, 0.0);

// ── Halftone helpers ──────────────────────────────────────────────────────────
mat2 rotate(float r) {
	float c = cos(r), s = sin(r);
	return mat2(vec2(c, s), vec2(-s, c));
}

float f(float value, vec2 uv, float radius) {
	return smoothstep(-1.0, 1.0,
		sqrt(value) - length(2.0 * fract(rotate(radius) * (frequency * (uv + uv_offset))) - 1.0));
}

// ── Paper helpers ─────────────────────────────────────────────────────────────
float rand(vec2 co) {
	return fract(sin(dot(co, vec2(12.9898, 78.233))) * 43758.5453);
}
float noise(vec2 p) {
	vec2 i = floor(p), fv = fract(p);
	fv = fv * fv * (3.0 - 2.0 * fv);
	return mix(mix(rand(i),              rand(i + vec2(1, 0)), fv.x),
	           mix(rand(i + vec2(0, 1)), rand(i + vec2(1, 1)), fv.x), fv.y);
}
float fbm(vec2 p) {
	return noise(p) * 0.5 + noise(p * 2.1) * 0.25 + noise(p * 4.3) * 0.125 + noise(p * 8.7) * 0.063;
}

void fragment() {
	vec3 color = texture(SCREEN_TEXTURE, SCREEN_UV).rgb;

	// Paper UV — tweened by code so effectUV == SCREEN_UV at transition start,
	// then converges to UV at full-page scale (wider paper texture when zoomed out).
	vec2 effectUV = UV * uv_noise_scale + uv_noise_offset;

	// ── Halftone ──────────────────────────────────────────────────────────────
	vec4 cmyk = vec4(vec3(1.0) - color, 1.0 - max(max(color.r, color.g), color.b));
	cmyk.xyz -= cmyk.w;
	float c = f(cmyk.x, effectUV, radius_c);
	float m = f(cmyk.y, effectUV, radius_m);
	float y = f(cmyk.z, effectUV, radius_y);
	float k = f(cmyk.w, effectUV, radius_k);
	vec3 halftone = mix(vec3(1.0) - 0.9 * vec3(c, m, y), vec3(0.0), 0.85 * k);
	float luma = dot(color, vec3(0.299, 0.587, 0.114));
	float luma_guard = smoothstep(halftone_luma_min - 0.05, halftone_luma_min + 0.05, luma)
	                 * (1.0 - smoothstep(halftone_luma_max - 0.05, halftone_luma_max + 0.05, luma));
	vec3 col = mix(color, halftone, halftone_blend * luma_guard);

	// ── Ink bleed ─────────────────────────────────────────────────────────────
	float closest = 1.0;
	vec3  nearestLineColor = vec3(0.0);
	float ga = 2.399963;
	for (int i = 0; i < 32; i++) {
		float r     = sqrt(float(i + 1) / 32.0);
		float angle = float(i) * ga;
		float nOff  = fbm(effectUV * 10.0 + vec2(float(i) * 0.3)) * 1.5;
		vec2  offset = vec2(cos(angle + nOff), sin(angle + nOff)) * r * SCREEN_PIXEL_SIZE * glow_range;
		vec4  s = texture(SCREEN_TEXTURE, SCREEN_UV + offset);
		float b = dot(s.rgb, vec3(0.333));
		if (b < 0.45 && r < closest) {
			closest          = r;
			nearestLineColor = s.rgb;
		}
	}
	float glowAmount = pow(1.0 - closest, glow_falloff) * glow_strength;
	glowAmount *= (0.6 + fbm(effectUV * 15.0 + vec2(2.3, 5.1)) * 0.8);
	col = mix(col, nearestLineColor, clamp(glowAmount, 0.0, 1.0));

	// ── Dirt spots ────────────────────────────────────────────────────────────
	// UV-based so spots scale visibly with the page. dirtUV carries an offset set
	// by code on ComicPageLayer at transition end so the pattern is continuous.
	vec2  dirtUV = UV * uv_dirt_scale + uv_dirt_offset;
	float dirt = fbm(dirtUV * dirt_scale)
	           * fbm(dirtUV * dirt_scale * 2.3 + vec2(4.1, 2.7))
	           * fbm(dirtUV * dirt_scale * 0.5 + vec2(1.2, 8.3));
	col -= pow(dirt, 1.5) * dirt_strength;

	// ── Contrast ──────────────────────────────────────────────────────────────
	col = (col - 0.5) * contrast + 0.5;

	// ── Sepia ─────────────────────────────────────────────────────────────────
	float gray = dot(col, vec3(0.299, 0.587, 0.114));
	col = mix(col, vec3(gray) * vec3(1.12, 0.95, 0.70), sepia_strength);

	// ── Parchment noise ───────────────────────────────────────────────────────
	col += (fbm(effectUV * noise_scale)                         - 0.5) * 0.05;
	col += (fbm(effectUV * noise_scale * 2.5 + vec2(3.7, 1.9)) - 0.5) * 0.025;

	// ── Horizontal paper grain ────────────────────────────────────────────────
	col += sin(effectUV.y * 500.0) * 0.015 + sin(effectUV.y * 180.0) * 0.007;

	// ── Vignette ──────────────────────────────────────────────────────────────
	vec2 e = effectUV * (1.0 - effectUV);
	col *= pow(clamp(e.x * e.y * 16.0, 0.0, 1.0), vignette_strength);

	COLOR = vec4(col, 1.0);
}
