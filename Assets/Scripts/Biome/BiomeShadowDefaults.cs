using UnityEngine;


// Per-biome default shadow configuration. Used by BiomeManager to auto-configure the shadow overlay when switching biomes.
[System.Serializable]
public struct BiomeShadowDefaults
{
    public bool shadowEnabled;


    /// Index into BiomeManager.shadowPrefabs array.
    /// 0 = default shadow. Add more slots when you need different shadows per biome.

    public int shadowPrefabIndex;

    public static BiomeShadowDefaults ForBiome(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Grass:
                return new BiomeShadowDefaults { shadowEnabled = true, shadowPrefabIndex = 0 };

            case BiomeType.Snow:
                return new BiomeShadowDefaults { shadowEnabled = true, shadowPrefabIndex = 0 };

            case BiomeType.Desert:
                return new BiomeShadowDefaults { shadowEnabled = true, shadowPrefabIndex = 0 };

            case BiomeType.Wasteland:
                return new BiomeShadowDefaults { shadowEnabled = true, shadowPrefabIndex = 0 };

            case BiomeType.Stones:
                return new BiomeShadowDefaults { shadowEnabled = true, shadowPrefabIndex = 0 };

            case BiomeType.GrassCartoon:
                return new BiomeShadowDefaults { shadowEnabled = true, shadowPrefabIndex = 0 };

            case BiomeType.Marsh:
                return new BiomeShadowDefaults { shadowEnabled = true, shadowPrefabIndex = 0 };

            case BiomeType.Night:
                return new BiomeShadowDefaults { shadowEnabled = true, shadowPrefabIndex = 0 };

            default:
                return ForBiome(BiomeType.Grass);
        }
    }
}
