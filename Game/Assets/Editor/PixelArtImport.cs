using UnityEditor;
using UnityEngine;

namespace KingdomWatch.Game.Editor
{
    // Import settings for the pixel art in the private art submodule (#121),
    // kept here as code so they cannot drift between the two repositories:
    // nearest-neighbour sampling, no compression and no mipmaps, so an art
    // pixel stays one flat square at every whole-number zoom. ArtSet reads the
    // textures as they are and cuts them up at runtime, and its GPU crop into
    // the villager atlas needs them uncompressed RGBA32 on every platform.
    public sealed class PixelArtImport : AssetPostprocessor
    {
        // Bump to reimport everything under Assets/Art after changing a setting.
        public override uint GetVersion() => 1;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Art/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            // The villager sheets are 3584 px tall; the default cap of 2048
            // would halve them.
            importer.maxTextureSize = 4096;
            foreach (var platform in new[] { "Standalone", "Android", "iPhone" })
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                settings.format = TextureImporterFormat.RGBA32;
                settings.maxTextureSize = 4096;
                importer.SetPlatformTextureSettings(settings);
            }
        }
    }
}
