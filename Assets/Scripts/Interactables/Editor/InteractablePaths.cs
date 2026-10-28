#if UNITY_EDITOR
namespace CommodoreCircuit.Interactables.EditorTools
{
    /// <summary>Where the interactable art lives. Change here if you move folders.</summary>
    public static class InteractablePaths
    {
        public const string Models = "Assets/Art/Interactables/Models";
        public const string Textures = "Assets/Art/Interactables/Textures";
        public const string Icons = "Assets/Art/Interactables/Icons";
        public const string Materials = "Assets/Art/Interactables/Materials";
        public const string Prefabs = "Assets/Prefabs/Interactables";

        public const string Material = Materials + "/CC_Interactables.mat";
        public const string TrailMaterial = Materials + "/CC_FrisbeeTrail.mat";
        public const string Palette = Textures + "/CC_Palette.png";
        public const string PaletteEmission = Textures + "/CC_Palette_Emission.png";
        public const string ToonShader = "CommodoreCircuit/ToonLit";
    }
}
#endif
