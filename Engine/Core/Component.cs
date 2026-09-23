namespace ArcEngine.Engine.Core;

/// <summary>
/// Base class for everything attached to a <see cref="GameObject"/>.
/// Lifecycle (driven by <see cref="Scene.Update"/>):
/// <list type="number">
///   <item><c>Awake</c> — once, the first frame after attachment, before <c>Start</c>.</item>
///   <item><c>Start</c> — once, the first frame after Awake.</item>
///   <item><c>Update(dt)</c> — every frame.</item>
///   <item><c>LateUpdate(dt)</c> — every frame, after all <c>Update</c> calls.</item>
///   <item><c>OnDestroy</c> — once, when the owning GameObject is removed from the scene.</item>
/// </list>
/// </summary>
public abstract class Component
{
    /// <summary>Owning GameObject. Set by <see cref="GameObject.AddComponent{T}()"/>.</summary>
    public GameObject GameObject { get; internal set; } = null!;

    /// <summary>Convenience accessor for the sibling Transform.</summary>
    public Transform Transform => GameObject.Transform;

    // Internal lifecycle bookkeeping consumed by Scene.Update.
    internal bool _awakeCalled;
    internal bool _startCalled;

    public virtual void Awake() { }
    public virtual void Start() { }
    public virtual void Update(float deltaTime) { }
    public virtual void LateUpdate(float deltaTime) { }
    public virtual void OnDestroy() { }

    // Trigger callbacks — dispatched by <see cref="Physics.PhysicsWorld"/> when a
    // sibling <see cref="Physics.Rigidbody"/> flagged as <c>IsTrigger</c> begins,
    // continues, or ends broadphase overlap with another rigidbody. The parameter
    // is the OTHER rigidbody in the pair.
    public virtual void OnTriggerEnter(Physics.Rigidbody other) { }
    public virtual void OnTriggerStay (Physics.Rigidbody other) { }
    public virtual void OnTriggerExit (Physics.Rigidbody other) { }
}
