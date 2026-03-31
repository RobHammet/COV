shader_type canvas_item;
//render_mode unshaded;

uniform float speed;

void fragment(){
    
	COLOR = texture(TEXTURE, vec2(UV.x + TIME * speed, UV.y));

}
