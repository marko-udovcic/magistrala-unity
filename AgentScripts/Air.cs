using UnityEngine;
using Magistrala.EditorTools;
public static class Air
{
    public static string Shot(int i)
    {
        var cam = Camera.main; var cc = cam.GetComponent<TruckChaseCamera>(); if (cc) cc.enabled = false; cam.fieldOfView = 55;
        switch (i)
        {
            case 0: SceneSetup.Look(-4000, 450, -4700, -3700, 40, -5800); break;     // oblique over D8 toward Kozjak
            case 1: SceneSetup.Look(-2500, 900, -4300, -2500, 0, -5300); break;       // higher, steeper
            case 2: SceneSetup.Look(-5200, 350, -5100, -5000, 0, -5400); break;       // down on the fields
            case 3: SceneSetup.Look(-1000, 600, -4200, -1500, 40, -5500); break;      // Solin side
            case 4: SceneSetup.Look(-3000, 120, -5250, -2700, 40, -5700); break;      // low oblique near road (near look must not regress)
        }
        return $"ok {i}";
    }
}
