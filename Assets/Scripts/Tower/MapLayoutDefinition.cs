using System.Collections.Generic;
using UnityEngine;

// Defines one map layout
// Create via: Assets → Create → Game → Map Layout Definition
// LAYOUT TYPES
// Concentric  – classic rings around the core (like the original).
// Custom      – any arbitrary slot positions (corridor, spiral, etc.).
// AUGMENT BEHAVIOUR
//  "additional_tower_rings"  → only works when layoutType == Concentric.
//  "additional_tower_slots"  → works on ALL layout types.
// OBSTACLES
// Layout-specific terrain (walls, buildings, moats, rocks, mushrooms, ice floes…).
// Each obstacle can be rendered as a Rectangle, Circle, or Ellipse.
// Curved obstacles produce smooth navigation for enemies (single circle
// collider, no segment edges to snag on).
[CreateAssetMenu(fileName = "NewMapLayout", menuName = "Game/Map Layout Definition")]
public class MapLayoutDefinition : ScriptableObject
{
    //  identity 

    [Tooltip("Display name shown in the stage banner / debug logs.")]
    public string layoutName = "New Layout";

    [Tooltip("Short description for the inspector / design notes.")]
    [TextArea(2, 4)]
    public string description;

    //  layout type 

    public enum LayoutType { Concentric, Custom }

    [Tooltip("Concentric = radial rings (classic).\n" +
             "Custom = arbitrary slot positions defined below.")]
    public LayoutType layoutType = LayoutType.Concentric;

    //  concentric (rings) 

    [Tooltip("Ring configurations. Used only when layoutType == Concentric.")]
    public List<TowerDefenseMap.RingConfiguration> rings = new List<TowerDefenseMap.RingConfiguration>();

    //  custom (free-form) 

    [Tooltip("Absolute slot positions (world-space relative to map centre).\n" +
             "Used only when layoutType == Custom.")]
    public List<Vector2> customSlotPositions = new List<Vector2>();

    [Tooltip("Slot size (visual + collider radius) for custom positions.")]
    public float customSlotSize = 1.9f;

    //  bonus slots 

    [Tooltip("Extra slots revealed by the 'additional_tower_slots' augment.\n" +
             "Works for both Concentric and Custom layouts.")]
    public List<Vector2> bonusSlotPositions = new List<Vector2>();

    [Tooltip("Slot size for bonus positions.")]
    public float bonusSlotSize = 1.9f;

    //  obstacles 

    [Tooltip("Layout-specific terrain obstacles (walls, buildings, moats, rocks, mushrooms).\n" +
             "Can be rectangles, circles, or ellipses. Curved obstacles are STRONGLY\n" +
             "preferred for blockMovement=true because enemies navigate around them\n" +
             "without snagging on segment edges.\n" +
             "They do NOT interfere with biome decorations (trees, rocks).")]
    public List<LayoutObstacle> obstacles = new List<LayoutObstacle>();

    [Tooltip("Visual guide lines that follow the shape of the layout.\n" +
             "For Concentric layouts, leave empty — the rings draw themselves.\n" +
             "For Custom layouts, define one or more polylines that connect slot\n" +
             "positions to give the player visual structure (corridor lines, " +
             "spiral path, fortress perimeter, etc).")]
    public List<ConnectionLine> connectionLines = new List<ConnectionLine>();

    // A polyline (open or closed) drawn on the ground to visually link slots and indicate the structure of the layout.
    [System.Serializable]
    public class ConnectionLine
    {
        [Tooltip("Sequence of points the line passes through.")]
        public List<Vector2> points = new List<Vector2>();

        [Tooltip("If true, the line connects the last point back to the first.")]
        public bool closed = false;

        [Tooltip("Line colour (alpha controls transparency).")]
        public Color color = new Color(0.85f, 0.92f, 1.00f, 0.55f);

        [Tooltip("Line width in world units.")]
        public float width = 0.08f;
    }

    // Visual shape used to draw and collide the obstacle.
    // Rectangle = classic boxy wall/building (segmented colliders).
    // Circle    = single round obstacle (single CircleCollider2D — smooth for nav).
    // Ellipse   = stretched circle, still one smooth collider.
    // Crescent  = curved moon-shaped wall. Inner arc faces 'rotationDegrees'
    //             direction. Approximated with chained CircleColliders so
    //             enemies path around its convex outer side smoothly.
    public enum ObstacleShape
    {
        Rectangle = 0,
        Circle = 1,
        Ellipse = 2,
        Crescent = 3,
    }

    // An obstacle defined by centre, size, shape, and visual style.
    // Spawned by TowerDefenseMap when this layout becomes active.
    [System.Serializable]
    public struct LayoutObstacle
    {
        [Tooltip("Visual & physics shape. Circle/Ellipse use a single smooth\n" +
                 "collider so enemies glide around them. Rectangle uses\n" +
                 "segmented box colliders.")]
        public ObstacleShape shape;

        [Tooltip("Centre position in world space.")]
        public Vector2 position;

        [Tooltip("Width and height in world units.\n" +
                 "For Circle, only size.x is used (as diameter).\n" +
                 "For Ellipse, size.x = width, size.y = height.")]
        public Vector2 size;

        [Tooltip("Rotation around Z in degrees (0 = axis-aligned).\n" +
                 "Affects Rectangle and Ellipse. Ignored for Circle.")]
        public float rotationDegrees;

        [Tooltip("Visual fill colour. Alpha controls transparency.\n" +
                 "Used as a tint for the stone texture on Rectangles,\n" +
                 "and as the fill colour for Circles/Ellipses.")]
        public Color color;

        [Tooltip("Spawn a collider so enemies/projectiles physically stop at this obstacle.\n" +
                 "Pure visual decorations should leave this off.")]
        public bool blocksMovement;

        [Tooltip("Optional label shown in hierarchy (e.g. 'NorthWall', 'Stone_03').")]
        public string label;
    }
}

