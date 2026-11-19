using UnityEngine; using System.Linq;
public static class Stops {
  public static string Run() {
    var g = Resources.Load<Magistrala.Game.GameWorld>("MagistralaGame");
    return string.Join("\n", g.stops.Select(s => $"{s.id}|{s.name}|{s.kind}|pos {s.pos.x:F1},{s.pos.y:F1},{s.pos.z:F1}|bay {s.bay.x:F1},{s.bay.y:F1},{s.bay.z:F1} dir {s.bayDir.x:F2},{s.bayDir.y:F2} hl {s.bayHl} hw {s.bayHw}|{s.maxVehicle}"));
  }
}
