// The full-screen map's baked data (WorldMap.cs), written by the editor menu Magistrala/Karta: ispeci (Assets/Editor/KartaBaker.cs,
// also run at the end of Magistrala/Igra: podaci): the ground picture (sea / coast, hillshade from our terrain heights, vegetation
// and land use, the junction models' buildings), the town's building footprints as one flat mesh, and the place-name anchors.
// Roads, markers and all text are drawn live from GameWorld (sharp at any zoom).
// Frame: Unity x / z. The texture's pixel (0, 0) is the south-west corner `origin`, `size` metres east / north; the mesh's
// vertices are (Unity x, Unity z, 0).
using UnityEngine;

namespace Magistrala.Game
{
    public class KartaData : ScriptableObject
    {
        public const string ResourcePath = "Karta";
        /// the ground in the two styles (flat default, dark), stored gamma(gain × colour linearised twice): drawn tinted 1 / gain
        public Texture2D ground, groundDark;
        public float gain = 1, gainDark = 8;
        public Mesh buildings;
        public Vector2 origin, size;
        public float metresPerPixel = 2;
        /// place names (Kaštel Lukšić …) at the centre of their buildings; rank 0 = the largest
        public string[] placeNames = new string[0];
        public Vector2[] placePos = new Vector2[0];
        public int[] placeCount = new int[0];
        public int buildingCount;
        public string source;
    }
}
