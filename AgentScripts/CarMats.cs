using System.Linq; using UnityEditor; using UnityEngine;
public static class CarMats
{
    public static string List()
    {
        var names = new System.Collections.Generic.SortedSet<string>();
        foreach (var g in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Models/Vehicles/Traffic" }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)); if (!go) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m) names.Add(m.name);
        }
        return string.Join(", ", names);
    }
}
