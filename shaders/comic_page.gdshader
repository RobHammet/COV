shader_type canvas_item;

uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;

uniform float noise_scale   : hint_range(1.0, 10.0)  = 2.256;
uniform float dirt_strength : hint_range(0.0, 1.0)   = 0.559;
uniform float dirt_scale    : hint_range(1.0, 100.0) = 61.721;

uniform float black_bleed_range    : hint_range(1.0, 20.0) = 4.0;
uniform float black_bleed_strength : hint_range(0.0, 1.0)  = 0.15;

// ── Halftone ──────────────────────────────────────────────────────────────────
// Angles follow traditional CMYK offset printing:
//   cyan 15° (0.2618), magenta 75° (1.3090), yellow 90° (1.5708), black 45° (0.7854)
uniform float angle_c            = 0.2618;
uniform float angle_m            = 1.3090;
uniform float angle_y            = 1.5708;
uniform float angle_k            = 0.7854;
uniform float halftone_dot_px    : hint_range(1.0, 32.0)  = 3.5;
uniform float halftone_blend     : hint_range(0.0, 1.0)   = 0.2;
uniform float halftone_luma_min  : hint_range(0.0, 0.5)   = 0.08;
uniform float halftone_luma_max  : hint_range(0.5, 1.0)   = 0.99;
uniform float dot_hardness       : hint_range(0.01, 1.0)  = 0.10;
uniform float dot_gain           : hint_range(-0.3, 0.3)  = -0.02;
uniform float black_crush        : hint_range(0.0, 0.3)   = 0.0;

// effectUV = UV * uv_noise_scale + uv_noise_offset.
// Tweened by code so effectUV == SCREEN_UV at transition start,
// then converges to UV at full-page scale.
uniform vec2 uv_noise_scale  = vec2(1.0, 1.0);
uniform vec2 uv_noise_offset = vec2(0.0, 0.0);

// dirtUV = UV * uv_dirt_scale + uv_dirt_offset.
// Set on ComicPageLayer at transition end so dirt aligns with the page container's UV.
uniform vec2 uv_dirt_scale  = vec2(1.0, 1.0);
uniform vec2 uv_dirt_offset = vec2(0.0, 0.0);

mat2 ht_rotate(float r) {
	float c = cos(r), s = sin(r);
	return mat2(vec2(c, s), vec2(-s, c));
}
float ht_dot(float value, vec2 uv, float angle, float freq) {
	float d = sqrt(max(value + dot_gain, 0.0))
	        - length(2.0 * fract(ht_rotate(angle) * (freq * uv)) - 1.0);
	return smoothstep(-dot_hardness, dot_hardness, d);
}

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
	vec3 col = texture(SCREEN_TEXTURE, SCREEN_UV).rgb;
	vec2 effectUV = UV * uv_noise_scale + uv_noise_offset;

	// Black bleed — dark ink spreading outward from nearby edges into surrounding area
	float closest = 1.0;
	float ga = 2.399963;
	for (int i = 0; i < 24; i++) {
		float r    = sqrt(float(i + 1) / 24.0);
		float nOff = fbm(effectUV * 10.0 + vec2(float(i) * 0.3)) * 1.5;
		vec2  off  = vec2(cos(float(i) * ga + nOff), sin(float(i) * ga + nOff)) * r * SCREEN_PIXEL_SIZE * black_bleed_range;
		float b    = dot(texture(SCREEN_TEXTURE, SCREEN_UV + off).rgb, vec3(0.333));
		if (b < 0.45 && r < closest) closest = r;
	}
	float bleed = pow(1.0 - closest, 2.0) * black_bleed_strength;
	bleed *= 0.6 + fbm(effectUV * 15.0 + vec2(2.3, 5.1)) * 0.8;
	col = mix(col, vec3(0.0), clamp(bleed, 0.0, 1.0));

	// ── Halftone (CMYK, screen UV-space, screen-pixel-constant dot size) ────────
	float fw   = fwidth(UV.x);
	float freq = fw > 0.0001 ? 1.0 / (halftone_dot_px * fw) : 20.0;
	vec4 cmyk = vec4(vec3(1.0) - col,
	                 1.0 - max(max(col.r, col.g), col.b));
	cmyk.xyz -= cmyk.w;
	float c = ht_dot(cmyk.x, UV, angle_c, freq);
	float m = ht_dot(cmyk.y, UV, angle_m, freq);
	float y = ht_dot(cmyk.z, UV, angle_y, freq);
	float k = ht_dot(cmyk.w, UV, angle_k, freq);
	vec3 halftone    = mix(vec3(1.0) - 0.9 * vec3(c, m, y), vec3(0.0), k);
	float luma       = dot(col, vec3(0.299, 0.587, 0.114));
	float luma_guard = smoothstep(halftone_luma_min - 0.05, halftone_luma_min + 0.05, luma)
	                 * (1.0 - smoothstep(halftone_luma_max - 0.05, halftone_luma_max + 0.05, luma));
	col = mix(col, halftone, halftone_blend * luma_guard);

	// Black crush — pull near-blacks to pure black.
	float col_luma = dot(col, vec3(0.299, 0.587, 0.114));
	col *= smoothstep(0.0, black_crush + 0.001, col_luma);

	// ── Paper effects (after halftone so they sit on top of the dots) ────────────
	// Dirt spots
	vec2  dirtUV = UV * uv_dirt_scale + uv_dirt_offset;
	float dirt = fbm(dirtUV * dirt_scale)
	           * fbm(dirtUV * dirt_scale * 2.3 + vec2(4.1, 2.7))
	           * fbm(dirtUV * dirt_scale * 0.5 + vec2(1.2, 8.3));
	col -= pow(dirt, 1.5) * dirt_strength;

	// Parchment noise
	col += (fbm(effectUV * noise_scale)                         - 0.5) * 0.05;
	col += (fbm(effectUV * noise_scale * 2.5 + vec2(3.7, 1.9)) - 0.5) * 0.025;

	// Horizontal paper grain
	col += sin(effectUV.y * 500.0) * 0.015 + sin(effectUV.y * 180.0) * 0.007;

	COLOR = vec4(col, 1.0);
}
