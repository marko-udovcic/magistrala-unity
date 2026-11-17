using UnityEngine;
using Magistrala.EditorTools;
public static class Seas
{
    public static string Shot(int i, string path)
    {
        var cam = Camera.main; var cc = cam.GetComponent<TruckChaseCamera>(); if (cc) cc.enabled = false; cam.fieldOfView = 55;
        switch (i)
        {
            case 0: SceneSetup.Look(-4000, 22, -4390, -4000, -35, -3000); break;     // from the shore, low: sea out to the horizon
            case 1: SceneSetup.Look(-4000, 300, -4350, -4000, -35, -3500); break;      // aerial oblique
            case 2: SceneSetup.Look(-4000, 120, -3700, -3990, -35, -3500); break;     // steep down over the water
            case 3: SceneSetup.Look(-5200, 14, -4650, -4600, -35, -3400); break;      // coast from truck height
        }
        return $"ok {i}";
    }
}
