// The streets as the street lamps see them (PropsBuilder.Lamps, StreetLights.cs): every drawn edge of the graph + the streets
// outside the models with its kind (D8 / old road and the other through roads / village streets / ramps) and half width.
using System.Collections.Generic;
using System.Linq;

namespace Magistrala.EditorTools
{
    public static partial class StreetsBuilder
    {
        public class LampEdge { public long id; public List<double[]> pts; public double hw; public int kind, degA, degB; public bool walk, oneway; public string name; }
        /// kind: 0 D8, 1 the old road (ref 6137) and the other primary / secondary roads, 2 village streets, 3 ramps / motorways
        public static List<LampEdge> LampEdges()
        {
            LoadGraph(); LoadModels(); ApplyOutside();
            var deg = new Dictionary<long, int>();
            foreach (var e in edges) { deg.TryGetValue(e.a, out int a); deg[e.a] = a + 1; deg.TryGetValue(e.b, out int b); deg[e.b] = b + 1; }
            var r = new List<LampEdge>();
            for (int ei = 0; ei < edges.Count; ei++)
            {
                var e = edges[ei];
                if (e.pts == null || e.sp == null || e.pts.Count < 2 || hidden.Contains(ei) || e.closed || e.spur != null || e.spans.Any(s => s.k == "tunnel")) continue;
                if (!e.pts.Any(p => InArea(p[0], p[2]))) continue;
                int kind = IsD8(e) ? 0 : e.type == "ramp" || e.type == "motorway" ? 3
                    : e.rf.Split(';').Any(x => x.Trim() == "6137") || e.type == "primary" || e.type == "secondary" || e.type == "trunk" ? 1 : 2;
                r.Add(new LampEdge { id = e.id, pts = e.pts, hw = e.sp.halfWidth, kind = kind, walk = e.verge == "urban_kerb" && kind != 0, oneway = e.oneway, name = e.name,
                    degA = deg.TryGetValue(e.a, out int da) ? da : 1, degB = deg.TryGetValue(e.b, out int db) ? db : 1 });
            }
            return r;
        }
    }
}
