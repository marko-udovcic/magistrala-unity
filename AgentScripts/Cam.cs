using UnityEngine;
using Magistrala.EditorTools;
public static class Cam
{
    // top-down camera above game point (gx,gz) at height h above ground y; rotation so +game-x points right on screen
    public static string Top(double gx, double gy, double gz, double h, float fov)
    {
        var cam = Camera.main; var cc = cam.GetComponent<TruckChaseCamera>(); if (cc) cc.enabled = false;
        cam.fieldOfView = fov;
        cam.transform.position = W.U(gx, gy + h, gz);
        var east = W.U(gx + 1, gy, gz) - W.U(gx, gy, gz); // screen-up = world dir of game +x ... make east point up-screen? use east as 'up' so +x is up
        cam.transform.rotation = Quaternion.LookRotation(Vector3.down, east);
        return $"cam {cam.transform.position}";
    }
}
