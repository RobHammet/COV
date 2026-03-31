shader_type canvas_item;

uniform sampler2D sprite_texture;
uniform sampler2D normal_map;
uniform float dark_threshold;
uniform float light_strength;
uniform vec3 light_direction;
uniform bool is_flipped = false;

void fragment() {
    // Get the UV coordinates of the pixel
    vec2 uv = UV;

    // Sample the color from the sprite texture
    vec4 sprite_color = texture(sprite_texture, uv);
	
    // Sample the normal map and convert to tangent space
    vec3 normal = (texture(normal_map, uv).rgb * 2.0) - 1.0;

    // Normalize the interpolated normal
    vec3 interpolated_normal = normalize(normal);

	vec3 light_direction2 = light_direction;
	if (is_flipped) {
		light_direction2.x = -(light_direction.x);
	}
    float ndotl = max(dot(interpolated_normal, light_direction2), 0.0);

	

    // Apply the light intensity to the final color
    vec3 final_color = sprite_color.rgb * ndotl * light_strength;
	
	float interpolated_normal_darkness = (final_color.r + final_color.g + final_color.b) / 3.0;
	if (interpolated_normal_darkness < dark_threshold) {
		//final_color = COLOR;
		final_color = vec3(0.0,0.0,0.0); // vec4(final_color, sprite_color.a);
	} else {
    // Calculate the final color using the normal map
	    final_color = sprite_color.rgb; // sprite_color.rgb + interpolated_normal * 0.5 * strength;
		    // Output the final color
		     // vec4(final_color, sprite_color.a);
				
		
	}
    COLOR = vec4(final_color, sprite_color.a);
}
