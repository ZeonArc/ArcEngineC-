using OpenTK.Mathematics;

namespace ArcEngine.Engine.Core;

/// <summary>
/// Container for all <see cref="GameObject"/>s in the world. Drives component lifecycle
/// (Awake / Start / Update / LateUpdate / OnDestroy) and provides typed component
/// queries used by the renderer (cameras, lights, mesh renderers).
/// </summary>
public class Scene
{
    private readonly List<GameObject> _gameObjects = new();
    private readonly List<GameObject> _pendingDestroy = new();

    /// <summary>Global ambient term used by the lighting pass. (Replaces LightSet.Ambient.)</summary>
    public Vector3 Ambient = new Vector3(0.05f);

    /// <summary>
    /// Add a GameObject (and recursively all of its child transforms' owners) to the scene.
    /// </summary>
    public void Add(GameObject obj)
    {
        AddRecursive(obj);
    }

    private void AddRecursive(GameObject obj)
    {
        _gameObjects.Add(obj);
        obj.Scene = this;
        foreach (var childT in obj.Transform.Children)
        {
            // Component.GameObject back-ref now provides the parent recovery.
            AddRecursive(childT.GameObject);
        }
    }

    /// <summary>Queue a GameObject for destruction at the end of the current update tick.</summary>
    public void Remove(GameObject obj) => _pendingDestroy.Add(obj);

    public IReadOnlyList<GameObject> GetObjects() => _gameObjects;

    /// <summary>
    /// Drive component lifecycle. Call once per simulation step.
    /// </summary>
    public void Update(float deltaTime)
    {
        // Snapshot in case scripts mutate the GameObject list during the tick.
        var snapshot = _gameObjects.ToArray();

        // Phase 1: lazy Awake / Start.
        foreach (var go in snapshot)
        {
            foreach (var c in go.Components)
            {
                if (!c._awakeCalled)
                {
                    c.Awake();
                    c._awakeCalled = true;
                }
                if (!c._startCalled)
                {
                    c.Start();
                    c._startCalled = true;
                }
            }
        }

        // Phase 2: Update.
        foreach (var go in snapshot)
            foreach (var c in go.Components)
                c.Update(deltaTime);

        // Phase 3: LateUpdate.
        foreach (var go in snapshot)
            foreach (var c in go.Components)
                c.LateUpdate(deltaTime);

        // Phase 4: drain destruction queue.
        if (_pendingDestroy.Count > 0)
        {
            foreach (var go in _pendingDestroy)
            {
                foreach (var c in go.Components) c.OnDestroy();
                _gameObjects.Remove(go);
                go.Scene = null;
            }
            _pendingDestroy.Clear();
        }
    }

    /// <summary>First component of type <typeparamref name="T"/> in the scene, or null.</summary>
    public T? FindComponent<T>() where T : Component
    {
        foreach (var go in _gameObjects)
        {
            var c = go.GetComponent<T>();
            if (c != null) return c;
        }
        return null;
    }

    /// <summary>All components of type <typeparamref name="T"/> in the scene.</summary>
    public IEnumerable<T> FindComponents<T>() where T : Component
    {
        foreach (var go in _gameObjects)
            foreach (var c in go.GetComponents<T>())
                yield return c;
    }
}
