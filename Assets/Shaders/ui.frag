#version 330 core

// UI fragment shader - sample the atlas texture and modulate by the per-vertex
// tint, OR just emit the tint for solid-color quads. u_textureMode is a
// per-vertex float so a single batch can mix textured and untextured quads
// (widgets often stack a colored panel behind a textured icon).

in vec2  vUV;
in vec4  vColor;
in float vTextureMode;

out vec4 FragColor;

uniform sampler2D u_texture;

void main()
{
    vec4 c = vColor;
    if (vTextureMode > 0.5)
        c *= texture(u_texture, vUV);
    FragColor = c;
}
