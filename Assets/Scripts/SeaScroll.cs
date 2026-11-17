using UnityEngine;

namespace Magistrala
{
    /// scrolls the sea normal map (two directions alternate through the offset) — cheap moving water
    public class SeaScroll : MonoBehaviour
    {
        public Vector2 speed = new Vector2(0.012f, 0.007f);
        Material mat;
        void Start() { mat = GetComponent<Renderer>().material; }
        void Update()
        {
            if (!mat) return;
            float t = Time.time;
            mat.SetTextureOffset("_BumpMap", new Vector2(speed.x * t + Mathf.Sin(t * .05f) * .2f, speed.y * t));
        }
    }
}
