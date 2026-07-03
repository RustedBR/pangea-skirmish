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
        public float heightStep = 1.0f;

        public float TileUnitsW => (float)tilePixelW / pixelsPerUnit;
        public float TileUnitsH => (float)tilePixelH / pixelsPerUnit;
    }
}
