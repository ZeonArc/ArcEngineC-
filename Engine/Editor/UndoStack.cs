using OpenTK.Mathematics;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Editor;

/// <summary>
/// One reversible edit made in the editor. Both <see cref="Do"/> (redo) and
/// <see cref="Undo"/> must return the target to its exact prior state and be safe
/// to call repeatedly.
/// </summary>
public interface IEditCommand
{
    void Do();
    void Undo();
    string Description { get; }
}

/// <summary>
/// Global editor undo/redo. Editor UI paths (inspector edits, Add/Remove Component,
/// GameObject creation/deletion) push <see cref="IEditCommand"/> instances after the
/// user commits a change; Ctrl+Z / Ctrl+Y walk the stack.
///
/// Not thread-safe — the editor runs on the main thread.
/// </summary>
public static class UndoStack
{
    private const int MaxHistory = 128;

    private static readonly LinkedList<IEditCommand> s_undo = new();
    private static readonly Stack<IEditCommand> s_redo = new();

    /// <summary>
    /// Record a command that has ALREADY been applied. Clears the redo stack.
    /// Use <see cref="Execute"/> if you want the command applied AND recorded.
    /// </summary>
    public static void Push(IEditCommand cmd)
    {
        s_undo.AddLast(cmd);
        if (s_undo.Count > MaxHistory) s_undo.RemoveFirst();
        s_redo.Clear();
    }

    /// <summary>Apply <paramref name="cmd"/> and record it in the history.</summary>
    public static void Execute(IEditCommand cmd)
    {
        cmd.Do();
        Push(cmd);
    }

    public static bool CanUndo => s_undo.Count > 0;
    public static bool CanRedo => s_redo.Count > 0;

    public static void Undo()
    {
        if (s_undo.Count == 0) return;
        var cmd = s_undo.Last!.Value;
        s_undo.RemoveLast();
        cmd.Undo();
        s_redo.Push(cmd);
    }

    public static void Redo()
    {
        if (s_redo.Count == 0) return;
        var cmd = s_redo.Pop();
        cmd.Do();
        s_undo.AddLast(cmd);
    }

    public static void Clear()
    {
        s_undo.Clear();
        s_redo.Clear();
    }
}

// ============================================================================
// Concrete commands
// ============================================================================

/// <summary>
/// Reversible edit of the Transform TRS triple. Push AFTER the drag is committed
/// (e.g. on ImGui's IsItemDeactivatedAfterEdit) so intermediate drag frames don't
/// pollute the history.
/// </summary>
public sealed class SetTransformCommand : IEditCommand
{
    private readonly Transform _target;
    private readonly Vector3 _before, _after;
    private readonly TransformField _field;

    public enum TransformField { Position, Rotation, Scale }

    public SetTransformCommand(Transform target, TransformField field, Vector3 before, Vector3 after)
    {
        _target = target;
        _field = field;
        _before = before;
        _after = after;
    }

    public string Description => $"Set {_field}";

    public void Do()   => Apply(_after);
    public void Undo() => Apply(_before);

    private void Apply(Vector3 v)
    {
        switch (_field)
        {
            case TransformField.Position: _target.Position = v; break;
            case TransformField.Rotation: _target.Rotation = v; break;
            case TransformField.Scale:    _target.Scale    = v; break;
        }
    }
}

/// <summary>Add a Component of a specific concrete type to a GameObject; undo removes it.</summary>
public sealed class AddComponentCommand : IEditCommand
{
    private readonly GameObject _target;
    private readonly Type _type;
    private Component? _added;

    public AddComponentCommand(GameObject target, Type type)
    {
        if (!typeof(Component).IsAssignableFrom(type))
            throw new ArgumentException("Type must derive from Component", nameof(type));
        _target = target;
        _type = type;
    }

    public string Description => $"Add {_type.Name}";

    public void Do()
    {
        var addMethod = typeof(GameObject).GetMethods()
            .First(m => m.Name == "AddComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(_type);
        _added = (Component?)addMethod.Invoke(_target, null);
    }

    public void Undo()
    {
        if (_added != null) _target.RemoveComponent(_added);
    }
}

/// <summary>Remove a specific Component from a GameObject; undo re-adds a fresh one of the same type.</summary>
public sealed class RemoveComponentCommand : IEditCommand
{
    private readonly GameObject _target;
    private readonly Component _component;
    private Component? _restored;

    public RemoveComponentCommand(GameObject target, Component component)
    {
        _target = target;
        _component = component;
    }

    public string Description => $"Remove {_component.GetType().Name}";

    public void Do() => _target.RemoveComponent(_component);

    public void Undo()
    {
        // We can't resurrect the destroyed instance, so re-attach a default one.
        var addMethod = typeof(GameObject).GetMethods()
            .First(m => m.Name == "AddComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(_component.GetType());
        _restored = (Component?)addMethod.Invoke(_target, null);
    }
}

/// <summary>
/// Create an empty GameObject in the scene. Undo removes it (via Scene.Remove which
/// drains at end of the next update; the redo re-adds a fresh instance).
/// </summary>
public sealed class CreateGameObjectCommand : IEditCommand
{
    private readonly Scene _scene;
    private readonly string _name;
    private GameObject? _created;

    public CreateGameObjectCommand(Scene scene, string name = "GameObject")
    {
        _scene = scene;
        _name = name;
    }

    public string Description => $"Create '{_name}'";

    public void Do()
    {
        _created = new GameObject { Name = _name };
        _scene.Add(_created);
    }

    public void Undo()
    {
        if (_created != null) _scene.Remove(_created);
    }
}
