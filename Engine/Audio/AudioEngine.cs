using OpenTK.Audio.OpenAL;

namespace ArcEngine.Engine.Audio;

/// <summary>
/// Global audio device + context lifecycle. Owns one <c>ALDevice</c> and one
/// <c>ALContext</c> for the whole application, created lazily on first use and
/// torn down at shutdown.
///
/// Backend: OpenAL Soft (pulled in via the OpenTK 4 meta-package as
/// <c>OpenTK.Audio.OpenAL</c>). All positional audio, listener state, and
/// per-source parameters route through this backend.
/// </summary>
public static class AudioEngine
{
    private static ALDevice s_device;
    private static ALContext s_context;
    private static bool s_initialized;

    /// <summary>True once <see cref="EnsureInitialized"/> has succeeded.</summary>
    public static bool IsInitialized => s_initialized;

    /// <summary>
    /// Once EnsureInitialized has failed we stop retrying so we don't log the
    /// same missing-DLL message from every AudioSource.Awake in the scene.
    /// </summary>
    private static bool s_initFailed;

    /// <summary>
    /// Open the default audio device + context. Idempotent; safe to call from
    /// component Awake methods that need audio.
    ///
    /// Failure modes (all non-fatal - the audio subsystem just no-ops):
    /// <list type="bullet">
    ///   <item>OpenAL runtime not installed (openal32.dll missing). Fix on
    ///         Windows: install OpenAL Soft from https://openal.org/downloads/
    ///         or copy soft_oal.dll from OpenAL Soft's bin folder into the
    ///         .exe directory and rename it to openal32.dll.</item>
    ///   <item>No audio device present.</item>
    ///   <item>Driver refuses to create the context.</item>
    /// </list>
    /// </summary>
    public static void EnsureInitialized()
    {
        if (s_initialized || s_initFailed) return;

        try
        {
            s_device = ALC.OpenDevice(null);
        }
        catch (DllNotFoundException)
        {
            s_initFailed = true;
            Console.WriteLine("[AudioEngine] OpenAL runtime not found (openal32.dll). Audio disabled. Install OpenAL Soft or drop soft_oal.dll (renamed to openal32.dll) next to the .exe.");
            return;
        }
        catch (Exception ex)
        {
            s_initFailed = true;
            Console.WriteLine($"[AudioEngine] Unexpected error opening audio device: {ex.Message}. Audio disabled for this session.");
            return;
        }

        if (s_device.Handle == IntPtr.Zero)
        {
            s_initFailed = true;
            Console.WriteLine("[AudioEngine] Failed to open the default audio device - audio disabled for this session.");
            return;
        }

        var attributes = new int[] { 0 };
        try
        {
            s_context = ALC.CreateContext(s_device, attributes);
            if (!ALC.MakeContextCurrent(s_context))
            {
                s_initFailed = true;
                Console.WriteLine("[AudioEngine] Failed to make ALContext current.");
                ALC.CloseDevice(s_device);
                return;
            }

            // Default distance model - inverse-clamped is the standard "1/d"
            // falloff most engines expose to gameplay code.
            AL.DistanceModel(ALDistanceModel.InverseDistanceClamped);
        }
        catch (Exception ex)
        {
            s_initFailed = true;
            Console.WriteLine($"[AudioEngine] Context/state setup failed: {ex.Message}. Audio disabled.");
            try { ALC.CloseDevice(s_device); } catch { }
            return;
        }

        s_initialized = true;
        Console.WriteLine("[AudioEngine] Initialized OpenAL device.");
    }

    /// <summary>
    /// Tear down the audio device + context. Safe to call multiple times.
    /// Should run before process exit so the driver isn't left holding buffers.
    /// </summary>
    public static void Shutdown()
    {
        if (!s_initialized) return;

        ALC.MakeContextCurrent(ALContext.Null);
        ALC.DestroyContext(s_context);
        ALC.CloseDevice(s_device);
        s_initialized = false;
    }

    /// <summary>
    /// Log the most recent AL error (if any) at the given call site — useful when
    /// diagnosing "why isn't my sound playing".
    /// </summary>
    public static void CheckError(string label)
    {
        if (!s_initialized) return;
        var err = AL.GetError();
        if (err != ALError.NoError)
            Console.WriteLine($"[AudioEngine] {label}: AL error {err}");
    }
}
