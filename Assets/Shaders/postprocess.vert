#version 330 core

// Fullscreen-triangle vertex shader used by the post-processing chain
// (tonemap, bloom composite, FXAA, etc). Generates its own vertices from
// gl_VertexID - no VBO/VAO data needed; caller issues glDrawArrays(TRIANGLES, 0, 3).

out vec2 vUV;

void main()
{
    // The classic fullscreen triangle covers the screen in three vertices:
    //   ID 0 -> ( -1, -1 )   uv ( 0, 0 )
    //   ID 1 -> (  3, -1 )   uv ( 2, 0 )
    //   ID 2 -> ( -1,  3 )   uv ( 0, 2 )
    // The over-scan corners fall outside the clip volume and are clipped away.
    vec2 pos = vec2(
        (gl_VertexID == 1) ? 3.0 : -1.0,
        (gl_VertexID == 2) ? 3.0 : -1.0);
    vUV = pos * 0.5 + 0.5;
    gl_Position = vec4(pos, 0.0, 1.0);
}
