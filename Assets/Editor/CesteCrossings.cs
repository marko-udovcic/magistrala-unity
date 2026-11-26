using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Magistrala.EditorTools
{
    /// <summary>Surveyed zebra footprints, used only while baking kerbs. Coordinates are game metres.</summary>
    public static class CesteCrossings
    {
        struct Crossing { public Vector2 c, t; public float halfWidth, halfLength; }
        static readonly List<Crossing> crossings = new List<Crossing>();

        public static void Load(List<ModelJ> models)
        {
            crossings.Clear();
            var byFile = new Dictionary<string, ModelJ>();
            foreach (var m in models) byFile[Path.GetFileNameWithoutExtension(m.glb)] = m;
            foreach (var path in new[] { PaketData.KRIZANJA, PaketData.DIONICE })
            {
                if (!File.Exists(path)) { Debug.LogWarning("[Ceste] Nema podataka za rampe: " + path); continue; }
                var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(path)));
                foreach (var jo in MiniJson.L(MiniJson.G(root,"junctions") ?? MiniJson.G(root,"tiles")) ?? new List<object>())
                {
                    var j = MiniJson.O(jo);
                    if (!(MiniJson.G(j,"file") is string file) || !byFile.TryGetValue(file,out var m)) continue;
                    // Coordinator owns the Štafilić junction geometry.
                    if (m.key == "41" || m.key == "42") continue;
                    foreach (var oo in MiniJson.L(MiniJson.G(j,"objs")) ?? new List<object>())
                    {
                        var o=MiniJson.O(oo); if (MiniJson.G(o,"k") as string != "zebra") continue;
                        double x=MiniJson.D(o["x"]), z=MiniJson.D(o["z"]); m.Pad(ref x,ref z);
                        float a=MiniJson.F(o["a"]); var t=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                        if (m.lin != null)
                        {
                            double nx=m.lin[0]*x+m.lin[1]*z, nz=m.lin[2]*x+m.lin[3]*z; x=nx; z=nz;
                            t=new Vector2((float)(m.lin[0]*t.x+m.lin[1]*t.y),(float)(m.lin[2]*t.x+m.lin[3]*t.y)).normalized;
                        }
                        crossings.Add(new Crossing { c=new Vector2((float)(m.x+x),(float)(m.z+z)),t=t,
                            halfWidth=MiniJson.F(o["w"])*.5f,halfLength=MiniJson.F(MiniJson.G(o,"l") ?? 4.0)*.5f });
                    }
                }
            }
        }

        public static double KerbHeight(double x,double z,double full)
        {
            float weight=0;
            foreach (var c in crossings)
            {
                var d=new Vector2((float)x,(float)z)-c.c;
                float across=Mathf.Abs(d.x*c.t.y-d.y*c.t.x), along=Mathf.Abs(Vector2.Dot(d,c.t));
                float side=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(c.halfWidth+.3f,c.halfWidth+2.4f,across));
                float ramp=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(c.halfLength-.3f,c.halfLength+1.2f,along));
                weight=Mathf.Max(weight,side*ramp);
            }
            return Math.Min(full, .02 + (.15-.02)*(1-weight));
        }
    }
}
