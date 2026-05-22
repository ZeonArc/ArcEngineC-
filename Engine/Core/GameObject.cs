using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.Core;

public class GameObject
{
    public Transform Transform { get; }
    public Mesh? Mesh;
    public Material? Material;
    public string Name = "GameObject";

    public GameObject()
    {
        Transform = new Transform { Owner = this };
    }
}
