using UnityEditor;
using UnityEngine;

namespace PangeaSkirmish.EditorTools
{
    /// <summary>
    /// Ícones em pixel art da HUD (Resources/UI/Icons, gerados por scripts/gen_icons.py):
    /// sem suavização, sem compressão e sem mipmaps, para não borrar nem trocar cores.
    /// </summary>
    public class IconImportSettings : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("Resources/UI/Icons/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
        }
    }
}
