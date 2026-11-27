using System;
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.EditorTools
{
    // Render-only 2 cm fillet of GLB sidewalks. Colliders keep the original mountable profile.
    // Positions are in the transformed model frame; winding was already reflected into Unity.
    public sealed class CesteKerbs
    {
        const float R = .02f, M = .0058578644f;
        struct Edge { public float top, bottom; public Vector3 normal; }
        readonly Dictionary<(long,long), Edge> edges = new Dictionary<(long,long), Edge>();
        static (long,long) Key(Vector3 p) => ((long)Math.Round(p.x*1000),(long)Math.Round(p.z*1000));

        public CesteKerbs(Vector3[] p, int[] indices)
        {
            for (int t=0;t+2<indices.Length;t+=3)
            {
                var a=p[indices[t]]; var b=p[indices[t+2]]; var c=p[indices[t+1]];
                var n=Vector3.Cross(b-a,c-a).normalized;
                if (Mathf.Abs(n.y)>.2f) continue;
                foreach (var v in new[] {a,b,c})
                {
                    var key=Key(v);
                    if (!edges.TryGetValue(key,out var e)) e=new Edge {top=v.y,bottom=v.y};
                    e.top=Mathf.Max(e.top,v.y); e.bottom=Mathf.Min(e.bottom,v.y);
                    e.normal+=n; edges[key]=e;
                }
            }
        }
        float Depth(Vector3 p) => edges.TryGetValue(Key(p),out var e) && e.top-e.bottom>.06f ? e.top-p.y : R+1;
        Vector3 Outward(Vector3 p) => edges.TryGetValue(Key(p),out var e) ? new Vector3(e.normal.x,0,e.normal.z).normalized : Vector3.zero;
        Vector3 Bend(Vector3 p)
        {
            if (!edges.TryGetValue(Key(p),out var e) || e.top-e.bottom<=.06f) return p;
            float d=e.top-p.y;
            if (d>=R) return p;
            float inset=d>=M ? Mathf.Lerp(M,0,(d-M)/(R-M)) : Mathf.Lerp(R,M,Mathf.Clamp01(d/M));
            var n=e.normal; n.y=0;
            return p-n.normalized*inset;
        }
        public IEnumerable<Vector3[]> Triangles(Vector3 a, Vector3 b, Vector3 c, bool vertical)
        {
            if (!vertical) { yield return new[] {Bend(a),Bend(b),Bend(c)}; yield break; }
            // Each model side is a straight ruled quad, so depth varies linearly over its triangles.
            float da=Depth(a), db=Depth(b), dc=Depth(c);
            if (da>R && db>R && dc>R) { yield return new[] {a,b,c}; yield break; }
            // Barycentric interpolation supplies the sloping top's depth at newly clipped points.
            var ab=b-a; var ac=c-a;
            float d00=Vector3.Dot(ab,ab), d01=Vector3.Dot(ab,ac), d11=Vector3.Dot(ac,ac);
            float den=d00*d11-d01*d01;
            if (Mathf.Abs(den)<1e-10f) yield break;
            Vector3 Weights(Vector3 p)
            {
                var ap=p-a; float d20=Vector3.Dot(ap,ab), d21=Vector3.Dot(ap,ac);
                float v=(d11*d20-d01*d21)/den, w=(d00*d21-d01*d20)/den;
                return new Vector3(1-v-w,v,w);
            }
            float D(Vector3 p) {var w=Weights(p);return da*w.x+db*w.y+dc*w.z;}
            var na=Outward(a);var nb=Outward(b);var nc=Outward(c);
            List<Vector3> Cut(List<Vector3> poly,float threshold,bool below)
            {
                var output=new List<Vector3>();
                for(int i=0;i<poly.Count;i++)
                {
                    var p=poly[i];var q=poly[(i+1)%poly.Count];float dp=D(p)-threshold,dq=D(q)-threshold;
                    bool ip=below ? dp>=0 : dp<=0, iq=below ? dq>=0 : dq<=0;
                    if(ip) output.Add(p);
                    if(ip!=iq) output.Add(Vector3.Lerp(p,q,dp/(dp-dq)));
                }
                return output;
            }
            foreach(var range in new[] {(R,100f),(M,R),(-.001f,M)})
            {
                var poly=Cut(Cut(new List<Vector3>{a,b,c},range.Item1,true),range.Item2,false);
                for(int i=0;i<poly.Count;i++)
                {
                    var p=poly[i];float d=D(p);
                    float inset=d>=R ? 0 : d>=M ? Mathf.Lerp(M,0,(d-M)/(R-M)) : Mathf.Lerp(R,M,Mathf.Clamp01(d/M));
                    // Shared endpoint directions keep adjoining bends and the inset top watertight.
                    var weights=Weights(p);
                    p-=(na*weights.x+nb*weights.y+nc*weights.z)*inset; poly[i]=p;
                }
                for(int i=1;i+1<poly.Count;i++) yield return new[]{poly[0],poly[i],poly[i+1]};
            }
        }
    }
}
