namespace ArcEngine.Engine.Core;

/// <summary>
/// Marker base class for user gameplay code. Inherits the full
/// <see cref="Component"/> lifecycle (Awake / Start / Update / LateUpdate / OnDestroy);
/// adds nothing on its own — it exists so gameplay components are easy to identify
/// (and so editor tooling, when it comes, can find them).
/// </summary>
public abstract class Script : Component
{
}
