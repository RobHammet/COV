shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform sampler2D use_texture;
uniform vec2 scale;
uniform vec2 offset;

uniform float brightness = .0824;
uniform float contrast = 1.0;
uniform float saturation = 1.0;


void fragment() {
	//vec3 c = textureLod(screen_texture, SCREEN_UV, 0.0).rgb;
	
    vec3 c = textureLod(use_texture, SCREEN_UV * (vec2(1.0, 1.0) / scale)  + offset, 0.0).rgb;
//	vec3 d = textureGather(sampler, coords)

    c.rgb = mix(vec3(0.0), c.rgb, brightness);
    c.rgb = mix(vec3(0.5), c.rgb, contrast);
    c.rgb = mix(vec3(dot(vec3(1.0), c.rgb) * 0.33333), c.rgb, saturation);

    COLOR.rgb = c;
}
