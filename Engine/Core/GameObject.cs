namespace ArcEngine.Engine.Core;

/// <summary>
/// A node in the scene. Holds a list of <see cref="Component"/> instances. Always has
/// exactly one <see cref="Transform"/>, auto-added by the constructor.
/// </summary>
public class GameObject
{
    public string Name = "GameObject";

    /// <summary>The mandatory Transform component. Set in the constructor.</summary>
    public Transform Transform { get; }

    /// <summary>The Scene this GameObject was added to, or null if unparented. Set by <see cref="Scene.Add"/>.</summary>
    public Scene? Scene { get; internal set; }

    private readonly List<Component> _components = new();

    public IReadOnlyList<Component> Components => _components;

    public GameObject()
    {
        // Auto-attach a Transform. We assign the property *before* calling AttachComponent
        // so any code that runs during attachment (currently none, but defensively) sees a
        // valid GameObject.Transform reference.
        var t = new Transform();
        Transform = t;
        AttachComponent(t);
    }

    /// <summary>Create and attach a new component of type <typeparamref name="T"/>.</summary>
    public T AddComponent<T>() where T : Component, new()
    {
        var c = new T();
        AttachComponent(c);
        return c;
    }

    /// <summary>Attach an externally-constructed component (useful when the component has a non-default ctor).</summary>
    public T AddComponent<T>(T component) where T : Component
    {
        AttachComponent(component);
        return component;
    }

    private void AttachComponent(Component c)
    {
        c.GameObject = this;
        _components.Add(c);
    }

    /// <summary>First component of type <typeparamref name="T"/> attached to this GameObject, or null.</summary>
    public T? GetComponent<T>() where T : Component
    {
        for (int i = 0; i < _components.Count; i++)
            if (_components[i] is T t) return t;
        return null;
    }

    /// <summary>All components of type <typeparamref name="T"/> attached to this GameObject.</summary>
    public IEnumerable<T> GetComponents<T>() where T : Component
    {
        for (int i = 0; i < _components.Count; i++)
            if (_components[i] is T t) yield return t;
    }

    /// <summary>
    /// Remove a component from this GameObject. Returns false if the component is the
    /// mandatory <see cref="Transform"/> (cannot be removed) or isn't attached.
    /// Calls <see cref="Component.OnDestroy"/> on success.
    /// </summary>
    public bool RemoveComponent(Component c)
    {
        if (c is Transform) return false; // every GameObject must have a Transform
        if (!_components.Remove(c)) return false;
        c.OnDestroy();
        return true;
    }
}
