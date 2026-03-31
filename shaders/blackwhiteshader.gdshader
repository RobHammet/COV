shader_type canvas_item;
uniform float white_cutoff : hint_range(0.0, 1.0) = 0.5;
void fragment() {
    vec4 sprite_color = texture(TEXTURE, UV);
	float clravg = (sprite_color.r + sprite_color.g + sprite_color.b) / 3.0;
	if (sprite_color.a > 0.0) {
		if (clravg > white_cutoff) {
		//	COLOR = vec4(1.0,1.0,1.0,sprite_color.a);
			COLOR = vec4(clravg,clravg,clravg,sprite_color.a);
		} else {
			COLOR = vec4(0.0,0.0,0.0,sprite_color.a);
		}
	}
	
}
