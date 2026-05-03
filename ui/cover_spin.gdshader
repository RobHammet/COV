shader_type canvas_item;

// X-axis rotation: 0 = facing viewer, PI/2 = laying flat on table.
// Positive angle: bottom of cover tilts toward viewer.
uniform float x_angle : hint_range(0.0, 3.141593)          = 0.0;
// Y-axis rotation: negative = right side toward viewer (shows right-edge pages).
uniform float y_angle : hint_range(-1.5707963, 1.5707963)  = 0.0;
// Camera distance in local pixels — larger = subtler perspective distortion.
uniform float depth   : hint_range(100.0, 8000.0)          = 2500.0;
// Polygon centre in local pixel space; rotation pivots here so the book
// centre stays fixed on screen rather than rotating about its top-left corner.
uniform vec2  center  = vec2(0.0, 0.0);

void vertex() {
    vec2  v  = VERTEX - center;
    float sx = sin(x_angle);
    float cx = cos(x_angle);
    float sy = sin(y_angle);
    float cy = cos(y_angle);

    // Combined X-then-Y rotation. z > 0 means toward viewer.
    // X-axis rotation brings the bottom (positive local-y) toward the viewer.
    // Y-axis rotation at negative y_angle brings the right side toward the viewer.
    float z  = -v.x * sy + v.y * sx * cy;
    float px =  v.x * cy + v.y * sx * sy;
    float py =  v.y * cx;

    float sc = depth / max(depth - z, 1.0);
    VERTEX = vec2(px, py) * sc + center;
}
