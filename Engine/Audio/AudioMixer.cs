namespace ArcEngine.Engine.Audio;

/// <summary>
/// One node in the audio mixer tree. Each bus has an independent
/// <see cref="Volume"/> and multiplies it against its parent's effective
/// volume to produce a single gain factor that <see cref="AudioSource"/>s route
/// their per-source volume through.
/// </summary>
public class AudioBus
{
    public string Name { get; }
    public AudioBus? Parent { get; }

    /// <summary>Local volume in [0..1]. 1 = passthrough.</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>Effective volume = local * parent.effective. Recomputed on each get.</summary>
    public float EffectiveVolume => Parent == null ? Volume : Volume * Parent.EffectiveVolume;

    public AudioBus(string name, AudioBus? parent = null)
    {
        Name = name;
        Parent = parent;
    }
}

/// <summary>
/// Global audio bus registry. Comes pre-populated with a Master bus and three
/// standard sub-buses (Music, SFX, UI) so gameplay code can immediately route
/// AudioSources through a category without extra setup. Additional buses can
/// be created via <see cref="CreateBus"/>.
/// </summary>
public static class AudioMixer
{
    public static readonly AudioBus Master = new("Master");
    public static readonly AudioBus Music  = new("Music", Master);
    public static readonly AudioBus SFX    = new("SFX",   Master);
    public static readonly AudioBus UI     = new("UI",    Master);

    private static readonly Dictionary<string, AudioBus> s_buses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "Master", Master },
            { "Music",  Music  },
            { "SFX",    SFX    },
            { "UI",     UI     },
        };

    /// <summary>Get the named bus, or the Master bus as a fallback for unknown names.</summary>
    public static AudioBus Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return Master;
        return s_buses.TryGetValue(name, out var bus) ? bus : Master;
    }

    /// <summary>Create a new custom bus parented under <paramref name="parent"/> (defaults to Master).</summary>
    public static AudioBus CreateBus(string name, AudioBus? parent = null)
    {
        if (s_buses.TryGetValue(name, out var existing)) return existing;
        var bus = new AudioBus(name, parent ?? Master);
        s_buses[name] = bus;
        return bus;
    }

    /// <summary>Iterate every bus (order not guaranteed).</summary>
    public static IEnumerable<AudioBus> AllBuses => s_buses.Values;
}
