shader_type canvas_item;

uniform sampler2D SCREEN_TEXTURE: hint_screen_texture, filter_linear_mipmap;
void fragment() {
    
	vec4 colors = textureLod(TEXTURE, UV, 0.0);
	float lightness = (colors.r + colors.g + colors.b) / 3.0;
	// Place fragment code here.
	float clr = (COLOR.r + COLOR.g + COLOR.b) /3.0f;
	if( clr < 0.4	 &&
		COLOR.a > 0.0) {
		COLOR = vec4(0.0,0.0,0.0,1.0);
	} else if (COLOR.a > 0.0) {
		COLOR = textureLod(SCREEN_TEXTURE, UV, 1.0);
	}
}
