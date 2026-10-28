#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CommodoreCircuit.Interactables.EditorTools
{
    /// <summary>
    /// Applies the right import settings automatically, so nobody has to remember them:
    ///   Models   - 1 unit = 1 m, axis conversion baked, no embedded materials/animations/cameras/lights.
    ///   Palette  - point filtering, no mipmaps, uncompressed (keeps swatches exact).
    ///   Icons    - UI sprites with mipmaps (stops shimmer on world-space VR canvases).
    /// Runs on every (re)import of files in these folders.
    /// </summary>
    public class InteractableAssetPostprocessor : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(InteractablePaths.Models)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;   // Blender Z-up -> Unity Y-up, baked into transforms
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
        }

        void OnPreprocessTexture()
        {
            var ti = (TextureImporter)assetImporter;
            if (assetPath.StartsWith(InteractablePaths.Textures))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
            }
            else if (assetPath.StartsWith(InteractablePaths.Icons))
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true;
                ti.filterMode = FilterMode.Trilinear;
                ti.wrapMode = TextureWrapMode.Clamp;
            }
        }
    }
}
#endif
