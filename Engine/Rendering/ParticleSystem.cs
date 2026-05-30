using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

using ArcEngine.Engine.Core;

using SnVec4 = System.Numerics.Vector4;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// Camera-facing billboard particle system. CPU-simulated; emits particles from the
/// owning GameObject's <see cref="Transform.Position"/> at <see cref="EmissionRate"/>
/// per second, simulates them in <see cref="Update"/>, and is rendered by the engine's
/// <see cref="Renderer"/> as a separate post-main pass with additive blending.
///
/// GPU layout (per-instance VBO at attribute locations 1-3):
/// <list type="bullet">
///   <item>1 — position (vec3) + size (float) packed as vec4</item>
///   <item>2 — color (vec4)</item>
/// </list>
/// The 4 quad corners are generated in the vertex shader from <c>gl_VertexID</c>
/// (no static quad VBO needed).
/// </summary>
public class ParticleSystem : Component, IDisposable
{
    // ---- Tunable parameters --------------------------------------------------

    public float EmissionRate = 40f;                  // particles per second
    public int   MaxParticles = 1024;

    public float StartLifetime = 2.0f;                // seconds

    public float StartSize = 0.25f;
    public float EndSize   = 0.05f;

    /// <summary>RGBA. With additive blending, alpha primarily controls intensity falloff.</summary>
    public Vector4 StartColor = new Vector4(1.0f, 0.7f, 0.25f, 1.0f);
    public Vector4 EndColor   = new Vector4(0.6f, 0.05f, 0.0f, 0.0f);

    public Vector3 StartVelocity   = new Vector3(0f, 1.5f, 0f);
    /// <summary>Each component is the +/- random spread added to the start velocity.</summary>
    public Vector3 VelocityRandom  = new Vector3(0.4f, 0.3f, 0.4f);

    /// <summary>Acceleration. Up-pointing for fire, down-pointing for sparks/smoke.</summary>
    public Vector3 Gravity = new Vector3(0f, 0.6f, 0f);

    /// <summary>Per-second velocity decay (1.0 = freeze instantly, 0 = no damping).</summary>
    public float Damping = 0.4f;

    // ---- CPU simulation state ------------------------------------------------

    private struct Particle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float   Age;
        public float   Lifetime;
    }

    private Particle[] _particles = Array.Empty<Particle>();
    private int _aliveCount;
    private float _emissionAccumulator;
    private readonly Random _rng = new Random(12345);

    /// <summary>Reused-across-frames buffer for the GPU upload — sized at MaxParticles*8.</summary>
    private float[] _instanceBufferCpu = Array.Empty<float>();

    // ---- GPU resources -------------------------------------------------------

    private int _vao;
    private int _instanceVbo;
    private int _instanceCapacity; // in particles
    private bool _resourcesReady;
    private bool _disposed;

    /// <summary>The number of currently-alive particles (read by the renderer).</summary>
    public int AliveCount => _aliveCount;

    /// <summary>
    /// Internal: pack (pos.xyz, size) and (color.rgba) into the cached buffer for upload.
    /// Returns the live element count; the buffer's first <c>aliveCount * 8</c> floats are valid.
    /// </summary>
    private int FillInstanceBuffer()
    {
        int needed = _aliveCount * 8;
        if (_instanceBufferCpu.Length < needed)
            _instanceBufferCpu = new float[System.Math.Max(needed, MaxParticles * 8)];

        for (int i = 0; i < _aliveCount; i++)
        {
            ref var p = ref _particles[i];
            float t = MathHelper.Clamp(p.Age / p.Lifetime, 0f, 1f);
            float size = MathHelper.Lerp(StartSize, EndSize, t);
            Vector4 col = Vector4.Lerp(StartColor, EndColor, t);

            int b = i * 8;
            _instanceBufferCpu[b + 0] = p.Position.X;
            _instanceBufferCpu[b + 1] = p.Position.Y;
            _instanceBufferCpu[b + 2] = p.Position.Z;
            _instanceBufferCpu[b + 3] = size;
            _instanceBufferCpu[b + 4] = col.X;
            _instanceBufferCpu[b + 5] = col.Y;
            _instanceBufferCpu[b + 6] = col.Z;
            _instanceBufferCpu[b + 7] = col.W;
        }
        return _aliveCount;
    }

    // ============================================================================
    // Lifecycle
    // ============================================================================

    public override void Awake()
    {
        _particles = new Particle[MaxParticles];
        _aliveCount = 0;
        _emissionAccumulator = 0f;
    }

    public override void Update(float deltaTime)
    {
        if (_particles.Length != MaxParticles)
        {
            // MaxParticles changed via inspector — resize buffer (preserve alive ones).
            var newArr = new Particle[MaxParticles];
            int copy = System.Math.Min(_aliveCount, MaxParticles);
            Array.Copy(_particles, newArr, copy);
            _particles = newArr;
            _aliveCount = copy;
        }

        // Step alive particles.
        for (int i = 0; i < _aliveCount; )
        {
            ref var p = ref _particles[i];
            p.Age += deltaTime;
            if (p.Age >= p.Lifetime)
            {
                // Swap-remove: replace this slot with the last alive particle.
                _particles[i] = _particles[_aliveCount - 1];
                _aliveCount--;
                continue; // don't increment i — re-process the swapped-in particle
            }

            // Velocity damping (exponential): v *= exp(-damping * dt) ≈ v * (1 - damping*dt)
            // for small dt. Use the closed form for stability at any dt.
            float dampScale = MathF.Exp(-Damping * deltaTime);
            p.Velocity = p.Velocity * dampScale + Gravity * deltaTime;

            p.Position += p.Velocity * deltaTime;
            i++;
        }

        // Emit new particles.
        _emissionAccumulator += deltaTime * EmissionRate;
        while (_emissionAccumulator >= 1f && _aliveCount < MaxParticles)
        {
            _emissionAccumulator -= 1f;
            Spawn();
        }
        // Cap accumulator so a long pause + resume doesn't dump 1000 particles at once.
        if (_emissionAccumulator > MaxParticles) _emissionAccumulator = MaxParticles;
    }

    private void Spawn()
    {
        ref var p = ref _particles[_aliveCount++];
        p.Position = Transform.Position;

        Vector3 rnd = new Vector3(
            (float)(_rng.NextDouble() * 2.0 - 1.0) * VelocityRandom.X,
            (float)(_rng.NextDouble() * 2.0 - 1.0) * VelocityRandom.Y,
            (float)(_rng.NextDouble() * 2.0 - 1.0) * VelocityRandom.Z);

        p.Velocity = StartVelocity + rnd;
        p.Age = 0f;
        p.Lifetime = StartLifetime;
    }

    public override void OnDestroy() => Dispose();

    // ============================================================================
    // GPU side — called by Renderer.RenderParticles
    // ============================================================================

    /// <summary>Lazily creates the VAO + per-instance VBO. Called from the renderer.</summary>
    internal void EnsureGpuResources()
    {
        if (_resourcesReady) return;

        _vao = GL.GenVertexArray();
        _instanceVbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
        _instanceCapacity = MaxParticles;
        GL.BufferData(BufferTarget.ArrayBuffer, _instanceCapacity * 8 * sizeof(float),
            IntPtr.Zero, BufferUsageHint.DynamicDraw);

        const int stride = 8 * sizeof(float);
        // Location 1: pos.xyz + size (vec4)
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribDivisor(1, 1);

        // Location 2: color (vec4)
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribDivisor(2, 1);

        GL.BindVertexArray(0);
        _resourcesReady = true;
    }

    /// <summary>Bind the system's VAO and issue a single instanced draw of the alive particles.</summary>
    internal void Draw()
    {
        if (!_resourcesReady || _aliveCount == 0) return;

        // Pack alive particle state into the cached CPU buffer.
        int count = FillInstanceBuffer();

        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
        if (_aliveCount > _instanceCapacity)
        {
            _instanceCapacity = System.Math.Max(_aliveCount, _instanceCapacity * 2);
            GL.BufferData(BufferTarget.ArrayBuffer, _instanceCapacity * 8 * sizeof(float),
                IntPtr.Zero, BufferUsageHint.DynamicDraw);
        }
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, count * 8 * sizeof(float), _instanceBufferCpu);

        GL.BindVertexArray(_vao);
        GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, _aliveCount);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_resourcesReady)
        {
            GL.DeleteBuffer(_instanceVbo);
            GL.DeleteVertexArray(_vao);
            _resourcesReady = false;
        }
        GC.SuppressFinalize(this);
    }
}
