using UnityEngine;

/// <summary>
/// Defines a reusable cluster composition blueprint.
/// Each member has a prefab slot index (which prefab from the biome's 3 slots),
/// a relative position offset, and a scale multiplier.
///
/// Create via: Assets → Create → Obstacles → Cluster Blueprint
///
/// Slot indices:
///   0 = primary/hero prefab (e.g. large tree)
///   1 = secondary prefab (e.g. small rock)
///   2 = tertiary prefab (e.g. different rock variant)
///   -1 = pick randomly from all available
/// </summary>
[CreateAssetMenu(fileName = "NewClusterBlueprint", menuName = "Obstacles/Cluster Blueprint")]
public class ObstacleClusterBlueprint : ScriptableObject
{
    [Tooltip("Display name for this cluster type (e.g. 'Tree Grove', 'Rock Outcrop').")]
    public string displayName = "Custom Cluster";

    [Tooltip("The members that make up this cluster.")]
    public ClusterMember[] members;

    [Tooltip("Random rotation of the entire cluster around its anchor (gives variety when placed multiple times).")]
    public bool randomizeRotation = true;

    [Tooltip("Random jitter applied to each member position (world units). " +
             "Adds organic variation so repeated placements don't look identical.")]
    [Range(0f, 0.5f)]
    public float positionJitter = 0.15f;

    [System.Serializable]
    public struct ClusterMember
    {
        [Tooltip("Which prefab slot to use: 0 = primary, 1 = secondary, 2 = tertiary, -1 = random.")]
        [Range(-1, 2)]
        public int prefabSlotIndex;

        [Tooltip("Position offset from cluster anchor (X = left/right, Y = forward/back in 2D).")]
        public Vector2 offset;

        [Tooltip("Scale multiplier relative to the obstacle base scale. 1.0 = full size.")]
        [Range(0.2f, 2f)]
        public float scaleMult;
    }
}
