// Riva review shots (no play mode, nothing saved): Štafilić, Novi, Lučica Kaštel Stari, Lukšić, Kambelovac, Gomilica, Sućurac —
// per place a pedestrian view along the riva (1.7 m) and a truck-cab view from the coast road (2.6 m) towards the sea.
// The shore point: walking north (Unity +z) from 250 m out at sea at the place's game x until the first ground over 0.3 m.
// -executeMethod or Riva: snimke; file names <dir>/<prefix>-<place>-<pjesak|kamion>.png, numbers in <dir>/<prefix>-brojke.md
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class RivaShots
    {
        public static readonly (string name, double gx, double gz)[] Places = {
            ("stafilic", -8856, -4400), ("novi", -8190, -4365), ("stari-lucica", -7750, -4557), ("luksic", -6050, -4667),
            ("kambelovac", -4480, -4476), ("gomilica", -3560, -4300), ("sucurac", -1231, -4141) };

        static float Ground(Vector3 p) => Physics.Raycast(new Vector3(p.x, 150, p.z), Vector3.down, out var h, 400, ~0, QueryTriggerInteraction.Ignore) ? h.point.y : 0;

        public static string Shots(string dir, string prefix)
        {
            Directory.CreateDirectory(dir);
            EditorUtility.audioMasterMute = true; AudioListener.volume = 0;
            Physics.SyncTransforms();
            var mb = Object.FindFirstObjectByType<MooredBoats>(); if (mb) mb.Init();
            var pc = Object.FindFirstObjectByType<Magistrala.Traffic.ParkedCars>(); if (pc) pc.Init();
            var cam = new GameObject("riva_cam").AddComponent<Camera>();
            var main = Camera.main; if (main) cam.CopyFrom(main); cam.nearClipPlane = .1f; cam.farClipPlane = 3000; cam.fieldOfView = 55;
            var sb = new System.Text.StringBuilder($"# Riva snimke {prefix}\n\n| kadar | batches | trokuti (k) | SetPass |\n|---|---|---|---|\n");
            foreach (var pl in Places)
            {
                var c = W.U(pl.gx, 0, pl.gz);
                // shore: from 250 m north to 250 m south of the place's z, the first point with ground < 0.1
                Vector3 s = c; bool found = false;
                for (float dz = -250; dz < 250; dz += 1) { var p = new Vector3(c.x, 0, c.z + dz); if (Ground(p) > .3f) { s = p + new Vector3(0, 0, -1); found = true; break; } }
                if (!found) { sb.AppendLine($"| {pl.name} | obala nije nađena | | |"); continue; }
                // eyes on dry land (a mole's tip can be the first ground: the next metres may be water again)
                Vector3 Dry(Vector3 p) { for (int k = 0; k < 80; k++, p.z += 1) if (Ground(p) > .3f) break; return p; }
                var eyeP = Dry(s + new Vector3(0, 0, 5)); eyeP.y = Ground(eyeP) + 1.7f;
                var lookP = s + new Vector3(28, 1.0f, -6);
                var eyeT = Dry(s + new Vector3(0, 0, 24)); eyeT.y = Ground(eyeT) + 2.6f;
                var lookT = s + new Vector3(0, 0, -45);
                foreach (var (tag, e, l) in new[] { ("pjesak", eyeP, lookP), ("kamion", eyeT, lookT) })
                {
                    cam.transform.SetPositionAndRotation(e, Quaternion.LookRotation(l - e));
                    BoatsBuilder.Render(cam, mb, pc, Path.Combine(dir, $"v2-{pl.name}-{tag}-{prefix}.png"));
                    sb.AppendLine($"| {pl.name}-{tag} | {UnityStats.batches} | {UnityStats.triangles / 1000.0:F0} | {UnityStats.setPassCalls} |");
                }
            }
            Object.DestroyImmediate(cam.gameObject);
            // totals of the riva layers
            int Tri(string root) { var r = GameObject.Find("World/" + root); if (!r) return 0; return r.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh && !f.name.StartsWith("lod1") && !f.name.StartsWith("lod2")).Sum(f => (int)(f.sharedMesh.GetIndexCount(0) / 3 + (f.sharedMesh.subMeshCount > 1 ? f.sharedMesh.GetIndexCount(1) / 3 : 0))); }
            int Cnt(string root) { var r = GameObject.Find("World/" + root); return r ? r.GetComponentsInChildren<Renderer>().Length : 0; }
            sb.AppendLine($"\nRivaLukobrani: {Cnt("RivaLukobrani")} renderera, {Tri("RivaLukobrani")} trokuta; RivaOprema: {Cnt("RivaOprema")} renderera (svi LOD), {Tri("RivaOprema")} trokuta LOD0; brodica: {(mb ? mb.boats.Length : 0)}");
            File.WriteAllText(Path.Combine(dir, "v2-brojke-" + prefix + ".md"), sb.ToString());
            return sb.ToString();
        }
    }
}
