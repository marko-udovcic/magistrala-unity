using System.Linq; using UnityEngine;
public static class BldStats {
  public static string Run() {
    var root = GameObject.Find("World/Buildings/town"); var sb = new System.Text.StringBuilder();
    long tT=0,tH=0; int nT=0,nH=0; long[] sub=new long[11];
    foreach (var mf in root.GetComponentsInChildren<MeshFilter>()) { var m=mf.sharedMesh; long t=0; for(int s=0;s<m.subMeshCount;s++){long c=m.GetIndexCount(s)/3; t+=c; if(m.subMeshCount==11) sub[s]+=c;}
      if (mf.name.StartsWith("houses")) {tH+=t;nH++;} else {tT+=t;nT++;} }
    sb.AppendLine($"town {nT} chunks {tT/1000}k; houses {nH} chunks {tH/1000}k; house submeshes: {string.Join(",", sub.Select(x=>x/1000+"k"))}");
    var cam=Camera.main; sb.AppendLine($"cam far {cam.farClipPlane} fov {cam.fieldOfView} lodBias {QualitySettings.lodBias} shadowDist {QualitySettings.shadowDistance}");
    var u = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset; if(u) sb.AppendLine($"urp shadowDist {u.shadowDistance} cascades {u.shadowCascadeCount}");
    return sb.ToString();
  }
}
