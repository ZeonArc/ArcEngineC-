using Hexa.NET.ImGui;

using ArcEngine.Engine.Audio;

namespace ArcEngine.Engine.Editor;

/// <summary>
/// Live per-bus volume sliders. Renders as its own ImGui window so users can
/// tweak the Master / Music / SFX / UI mix while play-testing. Toggle from
/// the View menu (or just call <see cref="Render"/> unconditionally).
/// </summary>
public static class AudioMixerWindow
{
    /// <summary>Whether the window is currently rendered.</summary>
    public static bool IsOpen = false;

    public static void Render()
    {
        if (!IsOpen) return;

        if (!ImGui.Begin("Audio Mixer", ref IsOpen))
        {
            ImGui.End();
            return;
        }

        foreach (var bus in AudioMixer.AllBuses)
        {
            float v = bus.Volume;
            string label = bus.Parent == null ? bus.Name : $"  {bus.Name} (→ {bus.Parent.Name})";
            if (ImGui.SliderFloat(label, ref v, 0f, 1f))
                bus.Volume = v;
            ImGui.SameLine();
            ImGui.TextDisabled($"eff: {bus.EffectiveVolume:F2}");
        }
        ImGui.End();
    }
}
