using System.Collections.Generic;

namespace ArcEngine.Engine.Core;

public class Scene
{
    private readonly List<GameObject> _gameObjects = new();

    /// <summary>
    /// Add a GameObject (and recursively all of its child transforms' owners) to the scene.
    /// This makes <c>scene.Add(parent)</c> register the whole sub-tree in one call.
    /// </summary>
    public void Add(GameObject obj)
    {
        AddRecursive(obj);
    }

    private void AddRecursive(GameObject obj)
    {
        _gameObjects.Add(obj);

        foreach (var childT in obj.Transform.Children)
        {
            if (childT.Owner != null)
            {
                AddRecursive(childT.Owner);
            }
        }
    }

    public List<GameObject> GetObjects() => _gameObjects;
}
