using System.Text.Json;
using System.Text.Json.Nodes;

using OpenTK.Windowing.GraphicsLibraryFramework;

namespace ArcEngine.Engine.Input;

/// <summary>
/// One rebindable input action — a named handle (e.g. <c>"Jump"</c>, <c>"MoveForward"</c>)
/// with a list of keys and mouse buttons that trigger it. Any pressed key / button in
/// either list activates the action.
/// </summary>
public class InputBinding
{
    public List<Keys> Keys { get; } = new();
    public List<MouseButton> MouseButtons { get; } = new();

    public InputBinding() { }
    public InputBinding(params Keys[] keys) { Keys.AddRange(keys); }
}

/// <summary>
/// Named-action layer on top of <see cref="InputManager"/>. Scripts query
/// <c>actions.IsPressed("Jump")</c> instead of hard-coding <c>Keys.Space</c>, and the
/// binding is loaded from disk (<see cref="LoadFromFile"/>) so users can rebind
/// without recompiling.
///
/// Not a replacement for <see cref="InputManager"/> — the underlying key/mouse polling
/// still lives there. This class only wraps a name-to-binding lookup for gameplay code.
/// </summary>
public class InputActions
{
    private readonly InputManager _input;
    private readonly Dictionary<string, InputBinding> _bindings = new(StringComparer.Ordinal);

    public InputActions(InputManager input)
    {
        _input = input;
    }

    /// <summary>Add or replace the binding for <paramref name="action"/>.</summary>
    public void Bind(string action, params Keys[] keys)
    {
        var b = new InputBinding();
        b.Keys.AddRange(keys);
        _bindings[action] = b;
    }

    /// <summary>Add or replace the binding for <paramref name="action"/> from a <see cref="InputBinding"/>.</summary>
    public void Bind(string action, InputBinding binding) => _bindings[action] = binding;

    /// <summary>Get the current binding, or null if the action has never been registered.</summary>
    public InputBinding? GetBinding(string action) =>
        _bindings.TryGetValue(action, out var b) ? b : null;

    /// <summary>Enumerate all currently-registered actions.</summary>
    public IEnumerable<KeyValuePair<string, InputBinding>> AllBindings => _bindings;

    /// <summary>True while any bound key/button for the action is held down.</summary>
    public bool IsPressed(string action)
    {
        if (!_bindings.TryGetValue(action, out var b)) return false;
        foreach (var k in b.Keys)         if (_input.IsKeyDown(k)) return true;
        foreach (var m in b.MouseButtons) if (_input.IsMouseButtonDown(m)) return true;
        return false;
    }

    /// <summary>True only on the frame the action transitions from released to pressed.</summary>
    public bool WasTriggered(string action)
    {
        if (!_bindings.TryGetValue(action, out var b)) return false;
        foreach (var k in b.Keys)         if (_input.WasKeyPressedThisFrame(k)) return true;
        foreach (var m in b.MouseButtons) if (_input.WasMouseButtonPressedThisFrame(m)) return true;
        return false;
    }

    /// <summary>
    /// Signed 1D axis: <c>+1</c> when <paramref name="positive"/> is held, <c>-1</c> when
    /// <paramref name="negative"/> is held, <c>0</c> when neither or both are held.
    /// </summary>
    public float GetAxis(string positive, string negative)
    {
        float v = 0f;
        if (IsPressed(positive)) v += 1f;
        if (IsPressed(negative)) v -= 1f;
        return v;
    }

    // ============================================================================
    // Persistence — bindings.json format
    // ============================================================================

    public void SaveToFile(string path)
    {
        var root = new JsonObject();
        foreach (var (name, binding) in _bindings)
        {
            var entry = new JsonObject();
            var keys = new JsonArray();
            foreach (var k in binding.Keys) keys.Add(k.ToString());
            entry["Keys"] = keys;

            var mouse = new JsonArray();
            foreach (var m in binding.MouseButtons) mouse.Add(m.ToString());
            if (mouse.Count > 0) entry["MouseButtons"] = mouse;

            root[name] = entry;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Merge bindings from a JSON file into this instance. Missing keys retain their
    /// prior binding (if any). Unknown enum names are logged and skipped.
    /// </summary>
    public void LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[InputActions] Bindings file not found: {path}");
            return;
        }

        var doc = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        if (doc == null) { Console.WriteLine($"[InputActions] Invalid bindings JSON: {path}"); return; }

        foreach (var (name, node) in doc)
        {
            if (node is not JsonObject entry) continue;
            var b = new InputBinding();

            if (entry["Keys"] is JsonArray ks)
            {
                foreach (var kn in ks)
                {
                    if (kn is JsonValue kv && Enum.TryParse<Keys>(kv.GetValue<string>(), out var key))
                        b.Keys.Add(key);
                    else Console.WriteLine($"[InputActions] Unknown key '{kn}' for action '{name}'.");
                }
            }

            if (entry["MouseButtons"] is JsonArray ms)
            {
                foreach (var mn in ms)
                {
                    if (mn is JsonValue mv && Enum.TryParse<MouseButton>(mv.GetValue<string>(), out var mb))
                        b.MouseButtons.Add(mb);
                    else Console.WriteLine($"[InputActions] Unknown mouse button '{mn}' for action '{name}'.");
                }
            }

            _bindings[name] = b;
        }
    }

    // ============================================================================
    // Default action set — installed by Game at startup.
    // ============================================================================

    /// <summary>
    /// Populate this InputActions with the engine's default bindings — the ones the
    /// FPS camera and editor hotkeys use. Anything already bound is overwritten.
    /// </summary>
    public void InstallDefaults()
    {
        Bind("MoveForward", Keys.W);
        Bind("MoveBackward", Keys.S);
        Bind("MoveLeft", Keys.A);
        Bind("MoveRight", Keys.D);
        Bind("MoveUp", Keys.E);
        Bind("MoveDown", Keys.Q);
        Bind("Jump", Keys.Space);
        Bind("Fire", Keys.LeftControl);   // Mouse button alternative added below.
        var fire = _bindings["Fire"];
        fire.MouseButtons.Add(MouseButton.Left);
    }
}
