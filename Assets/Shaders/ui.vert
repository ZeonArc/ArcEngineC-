#version 330 core

// UI batch vertex shader. Consumes screen-space vertices (in pixels, top-left
// origin) plus per-vertex UV, RGBA tint, and a texture-mode flag; passes them
// to the fragment shader after transforming to clip space via a caller-supplied
// orthographic matrix (u_projection is set to an orthographic projection sized
// to the current framebuffer).

layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec2 aUV;
layout (location = 2) in vec4 aColor;
layout (location = 3) in float aTextureMode;   // 0 = solid color, 1 = sample u_texture

uniform mat4 u_projection;

out vec2  vUV;
out vec4  vColor;
out float vTextureMode;

void main()
{
    vUV = aUV;
    vColor = aColor;
    vTextureMode = aTextureMode;
    gl_Position = u_projection * vec4(aPosition, 0.0, 1.0);
}
