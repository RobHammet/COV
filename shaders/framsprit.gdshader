shader_type canvas_item;

uniform sampler2D spritesheet;
uniform int num_frames;
uniform float animation_speed;

void fragment() {
    // Calculate the UV coordinates of the pixel
    vec2 uv = UV;

    // Calculate the size of a single frame in UV space
    vec2 frame_uv_size = vec2(1.0 / float(num_frames), 1.0);

    // Calculate the current frame index based on the animation speed
    int current_frame = int(floor(TIME * animation_speed)) % num_frames;

    // Calculate the UV offset for the current frame
    vec2 frame_uv_offset = frame_uv_size * vec2(float(current_frame), 0.0);

    // Calculate the UV coordinates for the current pixel within the current frame
    vec2 frame_uv = uv * frame_uv_size + frame_uv_offset;

    // Sample the color from the spritesheet using the calculated UV coordinates
    vec4 color = texture(spritesheet, frame_uv);

    // Output the color
    COLOR = color;
}
