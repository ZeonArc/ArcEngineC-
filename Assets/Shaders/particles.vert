#version 330 core

// Sprint 12 particles vertex shader.
// Draws a camera-facing billboard quad per particle. The 4 corners are generated
// from gl_VertexID so we don't need a static quad VBO. Per-instance data:
//   loc 1: vec4 (xyz = world-space position, w = size)
//   loc 2: vec4 color
// The triangle-strip order is BL, BR, TL, TR (so 0,1,2,3 forms two triangles).

layout (location = 1) in vec4 aPosSize;     // xyz = position, w = size
layout (location = 2) in vec4 aColor;

uniform mat4 view;
uniform mat4 projection;

out vec2 vUV;
out vec4 vColor;

void main()
{
    // Quad corner offsets (-1..1) keyed off gl_VertexID with TriangleStrip order.
    vec2 offset;
    if      (gl_VertexID == 0) offset = vec2(-1.0, -1.0);
    else if (gl_VertexID == 1) offset = vec2( 1.0, -1.0);
    else if (gl_VertexID == 2) offset = vec2(-1.0,  1.0);
    else                       offset = vec2( 1.0,  1.0);

    // Extract camera right (X) and up (Y) directly from the view matrix.
    // OpenTK's view matrix is row-major; columns 0/1 of the inverse-rotation
    // are read as rows of view's upper 3x3.
    vec3 camRight = vec3(view[0][0], view[1][0], view[2][0]);
    vec3 camUp    = vec3(view[0][1], view[1][1], view[2][1]);

    float size = aPosSize.w;
    vec3 worldPos = aPosSize.xyz
                  + camRight * (offset.x * size)
                  + camUp    * (offset.y * size);

    gl_Position = projection * view * vec4(worldPos, 1.0);

    vUV    = offset * 0.5 + 0.5;     // (0,0)..(1,1)
    vColor = aColor;
}
