using UnityEngine;

namespace PangeaSkirmish
{
    [CreateAssetMenu(fileName = "IsoConfig", menuName = "Pangea/IsoConfig")]
    public class IsoConfig : ScriptableObject
    {
        [Header("Dimensoes do tile isometrico (pixels do atlas)")]
        public int tilePixelW = 64;
        public int tilePixelH = 32;
        public int atlasBasePixels = 32;
        public int pixelsPerUnit = 32;

        [Header("Altura por nivel de elevacao")]
        // Cada nivel de tile = 0.5 unidade de world.
        // Brush "full" tem height=2 -> sobe 1.0 (1 sprite); "half" height=1 -> sobe 0.5 (meio sprite).
        public float heightStep = 0.5f;

        public float TileUnitsW => (float)tilePixelW / pixelsPerUnit;
        public float TileUnitsH => (float)tilePixelH / pixelsPerUnit;
    }
}
