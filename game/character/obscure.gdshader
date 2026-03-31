shader_type canvas_item;
uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;
uniform float x = 42;
uniform float y = 42;

uniform sampler2D darkmap;
uniform vec2 scale;
uniform vec2 offset;


void fragment() {
	
	vec3 c = textureLod(SCREEN_TEXTURE, SCREEN_UV, 0.0).rgb;
    vec4 screen_color = texture(SCREEN_TEXTURE, SCREEN_UV);
   // vec4 polygon_color = texture(TEXTURE, UV);
	vec3 sc = textureLod(TEXTURE, UV, 0.0).rgb;
	vec4 tex_color = textureLod(SCREEN_TEXTURE, SCREEN_UV, 0.0);
	
	vec4 darkmap_color = textureLod(darkmap, SCREEN_UV * scale + offset, 0.0); 
	vec4 hint_color = textureLod(SCREEN_TEXTURE, SCREEN_UV, 0.0); 
	vec2 coords = vec2(200, 200) * TEXTURE_PIXEL_SIZE;
	
	
	// 00
	// 10
	// 11
	// 01
	
	if (UV.x >= 350.0) {
		float darkmap_point = (darkmap_color.r + darkmap_color.g + darkmap_color.b) / 3.0;
		COLOR.rgb = COLOR.rgb; // = sc; // = screen_color * vec4(1.0, 1.0, 1.0, COLOR.a);
	} else {
		 COLOR = hint_color; // vec4(0.0,0.0,0.0,0.0); // screen_color;
	}
	
	
	vec2 pos = vec2(x, y) * TEXTURE_PIXEL_SIZE;



    vec2 pixel_size = 1.0 / vec2(textureSize(darkmap, 0));
    for(int iy = -1; iy <= 1; iy++)
    for(int ix = -1; ix <= 1; ix++)
    {
        vec2 pixel_off = vec2(float(ix), float(iy));
        vec4 tex = texture(darkmap, UV + pixel_off * pixel_size);
        if(tex.rgba == vec4(1.0, 1.0, 1.0, 1.0))
        {
            COLOR = vec4(0.0, 1.0, 0.0, 1.0);
           // break;
        }
		
		if (x < 300.0) {
			COLOR = COLOR * tex.rgba; // vec4(1.0, 0.0, 0.0, 1.0);
		}
    }

	
}

		//vec2 uv = vec2(SCREEN_UV.x, SCREEN_UV.y) * TEXTURE_PIXEL_SIZE; //SCREEN_UV - offset;
	 //  vec4 sprite_color = texture(sprite_texture, SCREEN_UV);
       //vec4 sprite_color = texture(sprite_texture, uvec4(SCREEN_UV.g,);
    //   if (sprite_color.a > 0.0 && polygon_color.a > 0.0) {
	
		 // * vec4(SCREEN_UV.r, SCREEN_UV.g, SCREEN_UV.r, COLOR.a);
		 //textureLod(SCREEN_TEXTURE, SCREEN_UV, 0.0);
     //  COLOR = vec4(darkening_factor, darkening_factor, darkening_factor, 1.0);  // Red color for intersection points
    //   } else {
    //       COLOR = screen_color;// vec4(0.0, 0.0, 0.0, 0.0); //polygon_color;  // Original color for non-intersection points
    //   }
//    if (screen_color.r > 0.5) {
 //       COLOR = vec4(0.0, 0.0, 0.0, 0.0); // Transparent color
  //  } else {
      //  COLOR = tex_color; // Original color
    //}
	
