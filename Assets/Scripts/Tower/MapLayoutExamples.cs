// BUILT-IN LAYOUTS with slot positions, obstacles, and layout-shaped connection lines that visually link the slots.
// Coordinate scale: the map radius is 10. Slot positions span roughly
// ±2.5 (inner) to ±8 (outer). Bonus slots can extend further.


using System.Collections.Generic;
using UnityEngine;

public static class MapLayoutExamples
{
    static readonly Color WALL_COLOR = new Color(0.30f, 0.34f, 0.42f, 0.95f);
    static readonly Color BUILDING_COLOR = new Color(0.42f, 0.38f, 0.32f, 0.95f);
    static readonly Color LINE_COLOR = new Color(0.85f, 0.92f, 1.00f, 0.55f);

    public static List<MapLayoutDefinition> CreateAll()
    {
        return new List<MapLayoutDefinition>
        {
            MakeConcentricClassic(),
            MakeChokepointCorridor(),
            MakeSpiralSiege(),
            MakeBreachedFortress(),
            MakeCrossroads(),
            MakeTheGauntlet(),
            MakeTheArena(),
            MakeGhostTown(),
            MakeMazeHallways(),
            MakeDiamondFormation(),
            MakePincerGrip(),
            MakeStonehenge(),
            MakeCrossroadsPillars(),
            MakeAsteroidBelt(),
            MakePinwheel(),
        };
    }

    // 01  CONCENTRIC CLASSIC  (rings draw themselves — no connection lines)
    public static MapLayoutDefinition MakeConcentricClassic()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Concentric Classic";
        d.description = "Three concentric rings. Symmetric and fair.";
        d.layoutType = MapLayoutDefinition.LayoutType.Concentric;
        d.rings = new List<TowerDefenseMap.RingConfiguration>
        {
            new TowerDefenseMap.RingConfiguration { radius = 4.20f, slotCount = 6,  slotSize = 1.9f, enabled = true },
            new TowerDefenseMap.RingConfiguration { radius = 7.70f, slotCount = 8,  slotSize = 1.9f, enabled = true },
            new TowerDefenseMap.RingConfiguration { radius = 11.20f, slotCount = 10, slotSize = 1.9f, enabled = true },
        };
        d.bonusSlotPositions = CirclePositions(12.50f, 8, 22.5f);
        d.bonusSlotSize = 1.9f;
        return d;
    }

    // 02  CHOKEPOINT CORRIDOR  (Custom + walls + horizontal guide lines)
    public static MapLayoutDefinition MakeChokepointCorridor()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Chokepoint Corridor";
        d.description = "Walls funnel enemies through a horizontal lane.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            // Top wall edge slots
            new Vector2(-7.00f, 4.62f), new Vector2(-3.50f, 4.62f), new Vector2(0.00f, 4.62f),
            new Vector2(3.50f, 4.62f), new Vector2(7.00f, 4.62f),
            // Bottom wall edge slots
            new Vector2(-7.00f, -4.62f), new Vector2(-3.50f, -4.62f), new Vector2(0.00f, -4.62f),
            new Vector2(3.50f, -4.62f), new Vector2(7.00f, -4.62f),
            // Lane flanks (close to core, inside the corridor)
            new Vector2(-9.80f, 0.00f), new Vector2(9.80f, 0.00f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(-9.80f, 4.62f), new Vector2(9.80f, 4.62f),
            new Vector2(-9.80f, -4.62f), new Vector2(9.80f, -4.62f),
            // Mid-corridor flanks at the wall row
            new Vector2(-5.60f, 0.00f), new Vector2(5.60f, 0.00f),
        };
        d.bonusSlotSize = 1.9f;

        // Two rows of short wall segments, each with gaps for enemies to pass through.
        // Local avoidance can navigate around 3-unit segments easily.
        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            // Top wall row at y=4.7 — three 2.5-unit segments, 1.5-unit gaps
            Wall("TopWall_L", pos:(-5.60f, 6.58f), size:(3.50f, 1.40f)),
            Wall("TopWall_M", pos:(0.00f, 6.58f), size:(3.50f, 1.40f)),
            Wall("TopWall_R", pos:(5.60f, 6.58f), size:(3.50f, 1.40f)),
            // Bottom wall row mirrors the top
            Wall("BotWall_L", pos:(-5.60f, -6.58f), size:(3.50f, 1.40f)),
            Wall("BotWall_M", pos:(0.00f, -6.58f), size:(3.50f, 1.40f)),
            Wall("BotWall_R", pos:(5.60f, -6.58f), size:(3.50f, 1.40f)),
        };

        // Two horizontal guide lines along the wall slot rows
        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(closed:false, points: new Vector2[] {
                new Vector2(-10.50f, 4.62f), new Vector2(10.50f, 4.62f),
            }),
            Line(closed:false, points: new Vector2[] {
                new Vector2(-10.50f, -4.62f), new Vector2(10.50f, -4.62f),
            }),
        };
        return d;
    }

    // 05  SPIRAL SIEGE  (Custom + spiral guide line)
    public static MapLayoutDefinition MakeSpiralSiege()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Spiral Siege";
        d.description = "Slots line both edges of an inward spiral path.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            // 14 slots paired along a 1.25-turn inward spiral
            new Vector2(-1.40f, 11.90f), new Vector2(1.40f, 11.90f),
            new Vector2(-10.50f, 1.37f), new Vector2(-9.79f, 4.07f),
            new Vector2(-3.33f, -8.58f), new Vector2(-5.77f, -7.18f),
            new Vector2(6.44f, -4.45f), new Vector2(4.45f, -6.44f),
            new Vector2(4.76f, 4.37f), new Vector2(6.16f, 1.93f),
            new Vector2(-2.62f, 4.37f), new Vector2(0.08f, 5.10f),
            new Vector2(-3.50f, -1.40f), new Vector2(-3.50f, 1.40f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            // Spread along the outer turns of the spiral, away from the
            // tightly packed inner slots and the core.
            new Vector2(8.40f, 5.60f), new Vector2(-8.40f, -5.60f),
            new Vector2(2.80f, -10.50f), new Vector2(-2.80f, 10.50f),
            new Vector2(10.50f, -2.80f), new Vector2(-10.50f, 2.80f),
        };
        d.bonusSlotSize = 1.9f;

        // Single spiral path as the guide line — matches slot count/turns
        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(closed:false, points: BuildSpiralPath(11.90f, 2.10f, 90f, 90f + 450f, 32)),
        };
        return d;
    }

    // 06  BREACHED FORTRESS  (Custom + walls + perimeter guide line)
    public static MapLayoutDefinition MakeBreachedFortress()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Breached Fortress";
        d.description = "Perimeter wall with 4 cardinal gap breaches. " +
                        "Gap-guard slots flank the openings.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            // Gap guards (flank the 4 cardinal breach openings)
            new Vector2(3.50f, 9.80f), new Vector2(-3.50f, 9.80f),
            new Vector2(3.50f, -9.80f), new Vector2(-3.50f, -9.80f),
            new Vector2(9.80f, 3.50f), new Vector2(9.80f, -3.50f),
            new Vector2(-9.80f, 3.50f), new Vector2(-9.80f, -3.50f),
            // Corner clusters
            new Vector2(9.80f, 9.80f), new Vector2(7.00f, 9.80f), new Vector2(9.80f, 7.00f),
            new Vector2(9.80f, -9.80f), new Vector2(7.00f, -9.80f), new Vector2(9.80f, -7.00f),
            new Vector2(-9.80f, 9.80f), new Vector2(-7.00f, 9.80f), new Vector2(-9.80f, 7.00f),
            new Vector2(-9.80f, -9.80f), new Vector2(-7.00f, -9.80f), new Vector2(-9.80f, -7.00f),
            // Interior fallback slots
            new Vector2(0.00f, 4.90f), new Vector2(0.00f, -4.90f),
            new Vector2(4.90f, 0.00f), new Vector2(-4.90f, 0.00f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            // Inside the fortress, away from the core (which sits at 0,0)
            // and away from the walls (which sit at radius ~9.8).
            // Two rings: inner (r=2.8) and middle (r=6.3).
            new Vector2(2.80f, 2.80f), new Vector2(-2.80f, 2.80f),
            new Vector2(2.80f, -2.80f), new Vector2(-2.80f, -2.80f),
            new Vector2(6.30f, 2.80f), new Vector2(-6.30f, 2.80f),
            new Vector2(6.30f, -2.80f), new Vector2(-6.30f, -2.80f),
        };
        d.bonusSlotSize = 1.9f;

        // Wall segments at the perimeter, each 2.5 units long with multiple
        // gap openings. Local avoidance handles short obstacles much better
        // than long walls, and multiple gaps give enemies several entry points.
        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            // North side — two segments with a wide center gap
            Wall("N_Wall_L", pos:(-7.70f, 10.92f), size:(3.50f, 1.12f)),
            Wall("N_Wall_R", pos:(7.70f, 10.92f), size:(3.50f, 1.12f)),
            // South side
            Wall("S_Wall_L", pos:(-7.70f, -10.92f), size:(3.50f, 1.12f)),
            Wall("S_Wall_R", pos:(7.70f, -10.92f), size:(3.50f, 1.12f)),
            // West side
            Wall("W_Wall_T", pos:(-10.92f, 7.70f), size:(1.12f, 3.50f)),
            Wall("W_Wall_B", pos:(-10.92f, -7.70f), size:(1.12f, 3.50f)),
            // East side
            Wall("E_Wall_T", pos:(10.92f, 7.70f), size:(1.12f, 3.50f)),
            Wall("E_Wall_B", pos:(10.92f, -7.70f), size:(1.12f, 3.50f)),
        };

        // Square perimeter as guide (with gaps where breaches are)
        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            // North wall span (with gap at center)
            Line(false, new Vector2[] { new Vector2(-10.50f, 9.80f), new Vector2(-3.50f, 9.80f) }),
            Line(false, new Vector2[] { new Vector2(3.50f, 9.80f), new Vector2(10.50f, 9.80f) }),
            // South wall span
            Line(false, new Vector2[] { new Vector2(-10.50f, -9.80f), new Vector2(-3.50f, -9.80f) }),
            Line(false, new Vector2[] { new Vector2(3.50f, -9.80f), new Vector2(10.50f, -9.80f) }),
            // West wall span
            Line(false, new Vector2[] { new Vector2(-9.80f, 10.50f), new Vector2(-9.80f, 3.50f) }),
            Line(false, new Vector2[] { new Vector2(-9.80f, -3.50f), new Vector2(-9.80f, -10.50f) }),
            // East wall span
            Line(false, new Vector2[] { new Vector2(9.80f, 10.50f), new Vector2(9.80f, 3.50f) }),
            Line(false, new Vector2[] { new Vector2(9.80f, -3.50f), new Vector2(9.80f, -10.50f) }),
        };
        return d;
    }

    // 07  CROSSROADS  (Custom + lane grid guide lines)

    public static MapLayoutDefinition MakeCrossroads()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Crossroads";
        d.description = "Slots sit at the intersections of a lane grid. " +
                        "AoE towers shine.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        // 8 slots forming a square grid (skipping the center where the core is)
        d.customSlotPositions = new List<Vector2>
        {
            // Top row (y = +5)
            new Vector2(-7.00f, 7.00f), new Vector2(0.00f, 7.00f), new Vector2(7.00f, 7.00f),
            // Middle row (y = 0) — skip center, that's the core
            new Vector2(-7.00f, 0.00f),                        new Vector2(7.00f, 0.00f),
            // Bottom row (y = -5)
            new Vector2(-7.00f, -7.00f), new Vector2(0.00f, -7.00f), new Vector2(7.00f, -7.00f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            // Outer corners — extend the grid one more step
            new Vector2(-11.20f, 11.20f), new Vector2(11.20f, 11.20f),
            new Vector2(-11.20f, -11.20f), new Vector2(11.20f, -11.20f),
            // Outer cardinal mids on the same outer ring
            new Vector2(0.00f, 11.20f), new Vector2(0.00f, -11.20f),
            new Vector2(-11.20f, 0.00f), new Vector2(11.20f, 0.00f),
        };
        d.bonusSlotSize = 1.9f;

        // Guide lines pass exactly through the slot rows/columns
        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            // 3 horizontal lines through the slot rows
            Line(false, new Vector2[] { new Vector2(-11.90f, 7.00f), new Vector2(11.90f, 7.00f) }),
            Line(false, new Vector2[] { new Vector2(-11.90f, 0.00f), new Vector2(11.90f, 0.00f) }),
            Line(false, new Vector2[] { new Vector2(-11.90f, -7.00f), new Vector2(11.90f, -7.00f) }),
            // 3 vertical lines through the slot columns
            Line(false, new Vector2[] { new Vector2(-7.00f, -11.90f), new Vector2(-7.00f, 11.90f) }),
            Line(false, new Vector2[] { new Vector2(0.00f, -11.90f), new Vector2(0.00f, 11.90f) }),
            Line(false, new Vector2[] { new Vector2(7.00f, -11.90f), new Vector2(7.00f, 11.90f) }),
        };
        return d;
    }

    // 08  THE GAUNTLET  (Custom + short deflector walls)

    public static MapLayoutDefinition MakeTheGauntlet()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "The Gauntlet";
        d.description = "Long zigzag path. Slots line each corridor run.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        // 3 horizontal "runs" at y = +8, +5, +3.5 (above core).
        // Final slots flank the core at y = -3, -6.
        d.customSlotPositions = new List<Vector2>
        {
            // Run 1 (top, y = +8) — left to right
            new Vector2(-9.80f, 11.20f), new Vector2(-4.20f, 11.20f),
            new Vector2(4.20f, 11.20f), new Vector2(9.80f, 11.20f),
            // Run 2 (y = +5) — right to left
            new Vector2(9.80f, 7.00f), new Vector2(4.20f, 7.00f),
            new Vector2(-4.20f, 7.00f), new Vector2(-9.80f, 7.00f),
            // Final approach (slots flanking the path on the way to the core)
            new Vector2(-9.80f, 2.80f), new Vector2(9.80f, 2.80f),
            new Vector2(-4.20f, -4.20f), new Vector2(4.20f, -4.20f),
            new Vector2(-4.20f, -8.40f), new Vector2(4.20f, -8.40f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(-11.90f, 9.10f), new Vector2(11.90f, 9.10f),
            new Vector2(-11.90f, 4.90f), new Vector2(11.90f, 4.90f),
            // Bottom flanks (covering the final approach lanes)
            new Vector2(-11.90f, -6.30f), new Vector2(11.90f, -6.30f),
        };
        d.bonusSlotSize = 1.9f;

        // Short wall segments with 1.5-unit gaps between them. The pattern still
        // funnels the threat alternately, but enemies have multiple navigation options.
        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            // Between run 1 and run 2 — segments biased to LEFT, gap on right
            Wall("Div1_L", pos:(-8.40f, 9.10f), size:(3.50f, 0.70f)),
            Wall("Div1_M", pos:(-2.80f, 9.10f), size:(3.50f, 0.70f)),
            // Between run 2 and run 3 — segments biased to RIGHT, gap on left
            Wall("Div2_M", pos:(2.80f, 4.90f), size:(3.50f, 0.70f)),
            Wall("Div2_R", pos:(8.40f, 4.90f), size:(3.50f, 0.70f)),
        };

        // Zigzag guide line passes through every slot row at the slot Y
        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(false, new Vector2[] {
                new Vector2(-11.90f, 11.20f),  new Vector2(11.90f, 11.20f),  // run 1
                new Vector2(11.90f, 7.00f),  new Vector2(-11.90f, 7.00f),  // run 2
                new Vector2(-11.90f, 2.80f),  new Vector2(11.90f, 2.80f),  // approach
            }),
        };
        return d;
    }

    // 09  THE ARENA  (Custom + moat + bridges + perimeter guide)
    public static MapLayoutDefinition MakeTheArena()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "The Arena";
        d.description = "Core on an island behind a moat. Only 4 bridges cross.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            // Bridge guards (just outside moat at cardinals)
            new Vector2(0.00f, 6.30f), new Vector2(0.00f, -6.30f),
            new Vector2(6.30f, 0.00f), new Vector2(-6.30f, 0.00f),
            // Outer perimeter ring
            new Vector2(11.20f, 0.00f), new Vector2(9.70f, 5.60f),
            new Vector2(5.60f, 9.70f), new Vector2(0.00f, 11.20f),
            new Vector2(-5.60f, 9.70f), new Vector2(-9.70f, 5.60f),
            new Vector2(-11.20f, 0.00f), new Vector2(-9.70f, -5.60f),
            new Vector2(-5.60f, -9.70f), new Vector2(0.00f, -11.20f),
            new Vector2(5.60f, -9.70f), new Vector2(9.70f, -5.60f),
            // Inner island ring
            new Vector2(2.58f, 2.58f), new Vector2(-2.58f, 2.58f),
            new Vector2(-2.58f, -2.58f), new Vector2(2.58f, -2.58f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            // Cardinals between island and outer ring
            new Vector2(0.00f, 9.10f), new Vector2(0.00f, -9.10f),
            new Vector2(9.10f, 0.00f), new Vector2(-9.10f, 0.00f),
            // Diagonals on the outer perimeter
            new Vector2(6.44f, 6.44f), new Vector2(-6.44f, 6.44f),
            new Vector2(6.44f, -6.44f), new Vector2(-6.44f, -6.44f),
        };
        d.bonusSlotSize = 1.9f;

        // No physical obstacles for The Arena — the moat/bridges are
        // communicated visually by the connection lines (outer perimeter +
        // inner island circle). Avoids cluttering the map with decorative
        // sprites that confused players.

        // Outer perimeter circle + inner island circle as guides
        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(closed:true, points: ApproximateCircle(11.20f, 0.00f, 0.00f, 36)),
            Line(closed:true, points: ApproximateCircle(3.64f, 0.00f, 0.00f, 16)),
        };
        return d;
    }

    // GHOST TOWN  (Custom + buildings)

    public static MapLayoutDefinition MakeGhostTown()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Ghost Town";
        d.description = "Buildings funnel enemies down streets. Slots sit at street junctions.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        // Slots sit at junctions of a 3×3 street grid (skipping center = core)
        // Streets run at x = ±4 and y = ±4. Junctions at all 8 combinations.
        d.customSlotPositions = new List<Vector2>
        {
            // Outer street ring (corners of map)
            new Vector2(-9.80f, 9.80f), new Vector2(0.00f, 9.80f), new Vector2(9.80f, 9.80f),
            new Vector2(-9.80f, 0.00f),                        new Vector2(9.80f, 0.00f),
            new Vector2(-9.80f, -9.80f), new Vector2(0.00f, -9.80f), new Vector2(9.80f, -9.80f),
            // Inner street junctions
            new Vector2(-4.90f, 4.90f), new Vector2(4.90f, 4.90f),
            new Vector2(-4.90f, -4.90f), new Vector2(4.90f, -4.90f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            // Cardinal mid-ring (street-facing)
            new Vector2(0.00f, 4.90f), new Vector2(0.00f, -4.90f),
            new Vector2(4.90f, 0.00f), new Vector2(-4.90f, 0.00f),
            // Diagonal mid-ring (corner-pointing, between inner & outer buildings)
            new Vector2(2.45f, 4.90f), new Vector2(-2.45f, 4.90f),
            new Vector2(2.45f, -4.90f), new Vector2(-2.45f, -4.90f),
        };
        d.bonusSlotSize = 1.9f;

        // 4 outer buildings + 4 inner buildings, all outside the core's
        // safety zone (3 units). Streets remain open at every junction.
        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            Building("NW_Building", pos:(-7.35f, 7.35f), size:(3.50f, 3.50f)),
            Building("NE_Building", pos:(7.35f, 7.35f), size:(3.50f, 3.50f)),
            Building("SW_Building", pos:(-7.35f, -7.35f), size:(3.50f, 3.50f)),
            Building("SE_Building", pos:(7.35f, -7.35f), size:(3.50f, 3.50f)),
            // Inner buildings — pushed out so they don't overlap core safe zone
            Building("Inner_NW", pos:(-7.35f, 2.45f), size:(2.10f, 2.10f)),
            Building("Inner_NE", pos:(7.35f, 2.45f), size:(2.10f, 2.10f)),
            Building("Inner_SW", pos:(-7.35f, -2.45f), size:(2.10f, 2.10f)),
            Building("Inner_SE", pos:(7.35f, -2.45f), size:(2.10f, 2.10f)),
        };

        // Street grid: lines pass through every slot row/column
        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            // Horizontal streets at y = -7, -3.5, 0, 3.5, 7
            Line(false, new Vector2[] { new Vector2(-11.90f, 9.80f),    new Vector2(11.90f, 9.80f) }),
            Line(false, new Vector2[] { new Vector2(-11.90f, 4.90f),  new Vector2(11.90f, 4.90f) }),
            Line(false, new Vector2[] { new Vector2(-11.90f, 0.00f),    new Vector2(11.90f, 0.00f) }),
            Line(false, new Vector2[] { new Vector2(-11.90f, -4.90f),  new Vector2(11.90f, -4.90f) }),
            Line(false, new Vector2[] { new Vector2(-11.90f, -9.80f),    new Vector2(11.90f, -9.80f) }),
            // Vertical streets at x = -7, -3.5, 0, 3.5, 7
            Line(false, new Vector2[] { new Vector2(-9.80f, -11.90f), new Vector2(-9.80f, 11.90f) }),
            Line(false, new Vector2[] { new Vector2(-4.90f, -11.90f), new Vector2(-4.90f, 11.90f) }),
            Line(false, new Vector2[] { new Vector2(0.00f, -11.90f), new Vector2(0.00f, 11.90f) }),
            Line(false, new Vector2[] { new Vector2(4.90f, -11.90f), new Vector2(4.90f, 11.90f) }),
            Line(false, new Vector2[] { new Vector2(9.80f, -11.90f), new Vector2(9.80f, 11.90f) }),
        };
        return d;
    }

    // MAZE HALLWAYS  (Custom + walls forming an H-pattern)

    public static MapLayoutDefinition MakeMazeHallways()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Maze Hallways";
        d.description = "Two vertical corridors connected by a horizontal one. " +
                        "Slots line the inside walls of each corridor.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        // Slots inside the H-shape, at the corridor walls
        d.customSlotPositions = new List<Vector2>
        {
            // Left corridor — vertical column of slots
            new Vector2(-7.70f, 9.80f), new Vector2(-7.70f, 5.60f),
            new Vector2(-7.70f, -5.60f), new Vector2(-7.70f, -9.80f),
            new Vector2(-4.90f, 9.80f), new Vector2(-4.90f, 5.60f),
            new Vector2(-4.90f, -5.60f), new Vector2(-4.90f, -9.80f),
            // Right corridor
            new Vector2(7.70f, 9.80f), new Vector2(7.70f, 5.60f),
            new Vector2(7.70f, -5.60f), new Vector2(7.70f, -9.80f),
            new Vector2(4.90f, 9.80f), new Vector2(4.90f, 5.60f),
            new Vector2(4.90f, -5.60f), new Vector2(4.90f, -9.80f),
            // Cross-corridor (horizontal connection through the middle)
            new Vector2(-2.10f, 2.10f), new Vector2(2.10f, 2.10f),
            new Vector2(-2.10f, -2.10f), new Vector2(2.10f, -2.10f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            // Cross-corridor edges and flanking the top/bottom corridor openings.
            // (0,0) is avoided — that's where the central core sits.
            new Vector2(0.00f, 6.30f), new Vector2(0.00f, -6.30f),
            new Vector2(-6.30f, 0.00f), new Vector2(6.30f, 0.00f),
            new Vector2(0.00f, 9.80f), new Vector2(0.00f, -9.80f),
        };
        d.bonusSlotSize = 1.9f;

        // Walls forming the corridors (short segments, easy to navigate around)
        // Left corridor: outer wall at x=-7, inner wall at x=-2.5
        // Right corridor: outer wall at x=+7, inner wall at x=+2.5
        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            // Left outer (-7) — top and bottom segments, gap in middle
            Wall("L_Out_T", pos:(-9.80f, 8.40f), size:(0.84f, 3.50f)),
            Wall("L_Out_B", pos:(-9.80f, -8.40f), size:(0.84f, 3.50f)),
            // Left inner (-2.5)
            Wall("L_In_T",  pos:(-3.50f, 8.40f), size:(0.84f, 3.50f)),
            Wall("L_In_B",  pos:(-3.50f, -8.40f), size:(0.84f, 3.50f)),
            // Right outer (+7)
            Wall("R_Out_T", pos:(9.80f, 8.40f), size:(0.84f, 3.50f)),
            Wall("R_Out_B", pos:(9.80f, -8.40f), size:(0.84f, 3.50f)),
            // Right inner (+2.5)
            Wall("R_In_T",  pos:(3.50f, 8.40f), size:(0.84f, 3.50f)),
            Wall("R_In_B",  pos:(3.50f, -8.40f), size:(0.84f, 3.50f)),
        };

        // Guide lines tracing the corridor shape
        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            // Left corridor outline
            Line(false, new Vector2[] {
                new Vector2(-9.80f, 11.20f), new Vector2(-9.80f, -11.20f),
            }),
            Line(false, new Vector2[] {
                new Vector2(-3.50f, 11.20f), new Vector2(-3.50f, -11.20f),
            }),
            // Right corridor outline
            Line(false, new Vector2[] {
                new Vector2(9.80f, 11.20f), new Vector2(9.80f, -11.20f),
            }),
            Line(false, new Vector2[] {
                new Vector2(3.50f, 11.20f), new Vector2(3.50f, -11.20f),
            }),
            // Horizontal cross-corridor
            Line(false, new Vector2[] {
                new Vector2(-3.50f, 0.00f), new Vector2(3.50f, 0.00f),
            }),
        };
        return d;
    }

    // DIAMOND FORMATION  (Custom — slots in a rotated grid, no obstacles)

    public static MapLayoutDefinition MakeDiamondFormation()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Diamond Formation";
        d.description = "Slots arranged in a diamond grid (45° rotated). " +
                        "Diagonal coverage at every angle.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        // Diamond ring 1 (small) at distance ~3.5 along diagonals
        // Diamond ring 2 (medium) at distance ~6
        // Diamond ring 3 (large) at distance ~8.5
        d.customSlotPositions = new List<Vector2>
        {
            // Inner diamond (4 cardinal-rotated points)
            new Vector2(4.90f, 0.00f), new Vector2(-4.90f, 0.00f),
            new Vector2(0.00f, 4.90f), new Vector2(0.00f, -4.90f),
            // Mid diamond — 4 corners + 4 edge midpoints (8 total)
            new Vector2(8.40f, 0.00f), new Vector2(-8.40f, 0.00f),
            new Vector2(0.00f, 8.40f), new Vector2(0.00f, -8.40f),
            new Vector2(5.88f, 5.88f), new Vector2(-5.88f, 5.88f),
            new Vector2(5.88f, -5.88f), new Vector2(-5.88f, -5.88f),
            // Outer diamond
            new Vector2(11.90f, 0.00f), new Vector2(-11.90f, 0.00f),
            new Vector2(0.00f, 11.90f), new Vector2(0.00f, -11.90f),
            new Vector2(8.40f, 8.40f), new Vector2(-8.40f, 8.40f),
            new Vector2(8.40f, -8.40f), new Vector2(-8.40f, -8.40f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(10.50f, 4.20f), new Vector2(4.20f, 10.50f),
            new Vector2(-10.50f, 4.20f), new Vector2(-4.20f, 10.50f),
            new Vector2(10.50f, -4.20f), new Vector2(4.20f, -10.50f),
            new Vector2(-10.50f, -4.20f), new Vector2(-4.20f, -10.50f),
        };
        d.bonusSlotSize = 1.9f;

        // Three nested diamond outlines
        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            // Inner diamond
            Line(closed:true, points: new Vector2[] {
                new Vector2(4.90f, 0.00f), new Vector2(0.00f, 4.90f),
                new Vector2(-4.90f, 0.00f), new Vector2(0.00f, -4.90f),
            }),
            // Mid diamond
            Line(closed:true, points: new Vector2[] {
                new Vector2(8.40f, 0.00f), new Vector2(0.00f, 8.40f),
                new Vector2(-8.40f, 0.00f), new Vector2(0.00f, -8.40f),
            }),
            // Outer diamond
            Line(closed:true, points: new Vector2[] {
                new Vector2(11.90f, 0.00f), new Vector2(0.00f, 11.90f),
                new Vector2(-11.90f, 0.00f), new Vector2(0.00f, -11.90f),
            }),
        };
        return d;
    }

    // PINCER GRIP  (Custom — slots in two arc clusters, threat from sides)

    public static MapLayoutDefinition MakePincerGrip()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Pincer Grip";
        d.description = "Slots in two arc clusters, left and right of core. " +
                        "Top/bottom undefended — enemies enter from above and below.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            // Left arc cluster — slots curving around the west flank
            new Vector2(-4.20f, 6.30f),
            new Vector2(-7.00f, 4.20f),
            new Vector2(-9.10f, 0.00f),
            new Vector2(-7.00f, -4.20f),
            new Vector2(-4.20f, -6.30f),
            new Vector2(-6.30f, 2.10f),
            new Vector2(-6.30f, -2.10f),
            new Vector2(-3.50f, 0.00f),
            // Right arc cluster — mirror
            new Vector2(4.20f, 6.30f),
            new Vector2(7.00f, 4.20f),
            new Vector2(9.10f, 0.00f),
            new Vector2(7.00f, -4.20f),
            new Vector2(4.20f, -6.30f),
            new Vector2(6.30f, 2.10f),
            new Vector2(6.30f, -2.10f),
            new Vector2(3.50f, 0.00f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            // Late-game slots that finally cover the top/bottom entry points
            new Vector2(-2.10f, 9.10f), new Vector2(2.10f, 9.10f),
            new Vector2(-2.10f, -9.10f), new Vector2(2.10f, -9.10f),
            // Wider top/bottom sentries to extend coverage further
            new Vector2(0.00f, 11.20f), new Vector2(0.00f, -11.20f),
            new Vector2(-5.60f, 9.10f), new Vector2(5.60f, 9.10f),
        };
        d.bonusSlotSize = 1.9f;

        // Two arc guide lines visualising the pincer shape
        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            // Left arc — half-circle from top-left to bottom-left
            Line(false, points: BuildArc(centerX: 0.00f, centerY: 0.00f, radius: 7.00f,
                startAngleDeg: 130f, endAngleDeg: 230f, segments: 16)),
            // Right arc — mirror
            Line(false, points: BuildArc(centerX: 0.00f, centerY: 0.00f, radius: 7.00f,
                startAngleDeg: -50f, endAngleDeg: 50f, segments: 16)),
        };
        return d;
    }

    // OBSTACLE HELPERS

    static MapLayoutDefinition.LayoutObstacle Wall(string label, (float x, float y) pos, (float w, float h) size)
    {
        return new MapLayoutDefinition.LayoutObstacle
        {
            position = new Vector2(pos.x, pos.y),
            size = new Vector2(size.w, size.h),
            rotationDegrees = 0f,
            color = WALL_COLOR,
            blocksMovement = true,
            label = label,
        };
    }

    static MapLayoutDefinition.LayoutObstacle Building(string label, (float x, float y) pos, (float w, float h) size)
    {
        return new MapLayoutDefinition.LayoutObstacle
        {
            position = new Vector2(pos.x, pos.y),
            size = new Vector2(size.w, size.h),
            rotationDegrees = 0f,
            color = BUILDING_COLOR,
            blocksMovement = true,
            label = label,
        };
    }

    // CONNECTION LINE HELPERS

    static MapLayoutDefinition.ConnectionLine Line(bool closed, Vector2[] points)
    {
        var line = new MapLayoutDefinition.ConnectionLine
        {
            closed = closed,
            color = LINE_COLOR,
            width = 0.08f,
            points = new List<Vector2>(points),
        };
        return line;
    }

    static MapLayoutDefinition.ConnectionLine Line(bool closed, List<Vector2> points)
    {
        var line = new MapLayoutDefinition.ConnectionLine
        {
            closed = closed,
            color = LINE_COLOR,
            width = 0.08f,
            points = new List<Vector2>(points),
        };
        return line;
    }

    static List<Vector2> ApproximateCircle(float radius, float cx, float cy, int segments)
    {
        // Auto-bump under-sampled circles so they render as smooth curves
        // (rather than visible polygons) at typical 2D camera distances.
        // Rule of thumb: ~1 segment per 0.3 world units of perimeter,
        // capped at 8 (so tiny circles still get some shape) and 96 (so we
        // don't waste vertices on big circles).
        int idealSegments = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * radius / 0.3f), 8, 96);
        if (segments < idealSegments) segments = idealSegments;

        var pts = new List<Vector2>();
        for (int i = 0; i < segments; i++)
        {
            float a = Mathf.Deg2Rad * (i * 360f / segments);
            pts.Add(new Vector2(
                Mathf.Round((cx + radius * Mathf.Cos(a)) * 100f) / 100f,
                Mathf.Round((cy + radius * Mathf.Sin(a)) * 100f) / 100f
            ));
        }
        return pts;
    }

    static List<Vector2> BuildSpiralPath(float rStart, float rEnd, float aStart, float aEnd, int segments)
    {
        var pts = new List<Vector2>();
        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)(segments - 1);
            float r = Mathf.Lerp(rStart, rEnd, t);
            float a = Mathf.Deg2Rad * Mathf.Lerp(aStart, aEnd, t);
            pts.Add(new Vector2(
                Mathf.Round(r * Mathf.Cos(a) * 100f) / 100f,
                Mathf.Round(r * Mathf.Sin(a) * 100f) / 100f
            ));
        }
        return pts;
    }

    static List<Vector2> BuildArc(float centerX, float centerY, float radius,
                                   float startAngleDeg, float endAngleDeg, int segments)
    {
        // Same auto-upgrade as ApproximateCircle, scaled by arc fraction.
        float arcFraction = Mathf.Abs(endAngleDeg - startAngleDeg) / 360f;
        int idealSegments = Mathf.Clamp(
            Mathf.CeilToInt(2f * Mathf.PI * radius * arcFraction / 0.3f), 4, 64);
        if (segments < idealSegments) segments = idealSegments;

        var pts = new List<Vector2>();
        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)(segments - 1);
            float a = Mathf.Deg2Rad * Mathf.Lerp(startAngleDeg, endAngleDeg, t);
            pts.Add(new Vector2(
                Mathf.Round((centerX + radius * Mathf.Cos(a)) * 100f) / 100f,
                Mathf.Round((centerY + radius * Mathf.Sin(a)) * 100f) / 100f
            ));
        }
        return pts;
    }

    // RING POSITION HELPER

    static List<Vector2> CirclePositions(float radius, int count, float offsetDeg)
    {
        var list = new List<Vector2>(count);
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.Deg2Rad * (i * 360f / count + offsetDeg);
            list.Add(new Vector2(
                Mathf.Round(radius * Mathf.Cos(angle) * 100f) / 100f,
                Mathf.Round(radius * Mathf.Sin(angle) * 100f) / 100f
            ));
        }
        return list;
    }

    // ================================================================
    // NEW LAYOUTS — round/curved obstacles (Circle + Crescent shapes).
    // All gaps are sized for boss enemies: minimum gap width between any
    // two obstacles is 3 world units (twice the typical boss collider
    // size). No concave pockets, no narrow chokes, no traps.
    // ================================================================

    // Palette — only colors actually used by current layouts.
    static readonly Color STONE_GREY = new Color(0.45f, 0.46f, 0.50f, 1.00f);
    static readonly Color LINE_WARM_AMBER = new Color(0.90f, 0.65f, 0.30f, 0.55f);
    static readonly Color LINE_COOL_BLUE = new Color(0.55f, 0.75f, 0.95f, 0.55f);

    // ----------------------------------------------------------------
    // 13. STONEHENGE — 8 outer stones + 4 inner stones, wide gaps
    // ----------------------------------------------------------------
    // Tweak: only 8 outer stones (down from 12) so each cardinal gap
    // is ~3.5 units wide. Inner ring stones sit on diagonals only.
    public static MapLayoutDefinition MakeStonehenge()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Stonehenge";
        d.description = "Eight large standing stones in an outer ring with " +
                        "four inner stones on the diagonals. Wide cardinal " +
                        "gaps for boss access.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            // Cardinal slots outside the ring (forward defense)
            new Vector2(0.00f, 8.00f),   new Vector2(0.00f, -8.00f),
            new Vector2(8.00f, 0.00f),   new Vector2(-8.00f, 0.00f),
            // Diagonal slots outside the ring
            new Vector2(5.50f, 5.50f),   new Vector2(-5.50f, 5.50f),
            new Vector2(5.50f, -5.50f),  new Vector2(-5.50f, -5.50f),
            // Inner cardinal slots (between core and inner stones)
            new Vector2(0.00f, 4.50f),   new Vector2(0.00f, -4.50f),
            new Vector2(4.50f, 0.00f),   new Vector2(-4.50f, 0.00f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(-3.00f, 8.00f),  new Vector2(3.00f, 8.00f),
            new Vector2(-3.00f, -8.00f), new Vector2(3.00f, -8.00f),
            new Vector2(-9.50f, 0.00f),  new Vector2(9.50f, 0.00f),
        };
        d.bonusSlotSize = 1.9f;

        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>();
        // OUTER ring: 8 big stones at radius 6.5, placed on the diagonals
        // (45° offsets) so the 4 cardinals are wide-open boss paths.
        float outerR = 6.5f;
        for (int i = 0; i < 8; i++)
        {
            float angDeg = i * 45f + 22.5f; // 22.5°, 67.5°, ... — between cardinals
            float angRad = angDeg * Mathf.Deg2Rad;
            Vector2 pos = new Vector2(Mathf.Cos(angRad) * outerR, Mathf.Sin(angRad) * outerR);
            d.obstacles.Add(Stone($"OuterStone_{i}", pos, 2.2f, STONE_GREY));
        }
        // INNER ring: 4 smaller stones on the diagonals at radius 3.5.
        // These create soft S-curve paths from cardinals into the core.
        for (int i = 0; i < 4; i++)
        {
            float angDeg = 45f + i * 90f;
            float angRad = angDeg * Mathf.Deg2Rad;
            Vector2 pos = new Vector2(Mathf.Cos(angRad) * 3.5f, Mathf.Sin(angRad) * 3.5f);
            d.obstacles.Add(Stone($"InnerStone_{i}", pos, 1.5f, STONE_GREY));
        }

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(true, ApproximateCircle(6.5f, 0f, 0f, 36)),
            Line(true, ApproximateCircle(3.5f, 0f, 0f, 24)),
        };
        return d;
    }


    // ----------------------------------------------------------------
    // 15. CROSSROADS PILLARS (inverted) — open cardinal cross, pillars on diagonals
    // ----------------------------------------------------------------
    // Original idea had pillars on cardinals which trapped boss approaches.
    // INVERTED: cardinals are wide-open highways, pillars sit on the
    // diagonals where bosses would naturally avoid. Small arches between
    // adjacent diagonal pillars add visual structure.
    public static MapLayoutDefinition MakeCrossroadsPillars()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Crossroads Pillars";
        d.description = "Open cardinal highways for boss approaches, with " +
                        "pillars and arches on the diagonals. Slots dominate " +
                        "the open cross.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            // Slots along the OPEN cardinal cross — these get to shoot
            // at bosses head-on.
            new Vector2(0.00f, 8.00f),   new Vector2(0.00f, -8.00f),
            new Vector2(8.00f, 0.00f),   new Vector2(-8.00f, 0.00f),
            new Vector2(0.00f, 5.00f),   new Vector2(0.00f, -5.00f),
            new Vector2(5.00f, 0.00f),   new Vector2(-5.00f, 0.00f),
            new Vector2(0.00f, 3.00f),   new Vector2(0.00f, -3.00f),
            new Vector2(3.00f, 0.00f),   new Vector2(-3.00f, 0.00f),
            // Slots between cardinal cross arms and the diagonal pillars
            new Vector2(2.50f, 5.00f),   new Vector2(-2.50f, 5.00f),
            new Vector2(2.50f, -5.00f),  new Vector2(-2.50f, -5.00f),
            new Vector2(5.00f, 2.50f),   new Vector2(-5.00f, 2.50f),
            new Vector2(5.00f, -2.50f),  new Vector2(-5.00f, -2.50f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(0.00f, 9.50f),   new Vector2(0.00f, -9.50f),
            new Vector2(9.50f, 0.00f),   new Vector2(-9.50f, 0.00f),
            // Bonus slots tucked between pillar pairs on the diagonals
            new Vector2(4.20f, 4.20f),   new Vector2(-4.20f, 4.20f),
            new Vector2(4.20f, -4.20f),  new Vector2(-4.20f, -4.20f),
        };
        d.bonusSlotSize = 1.9f;

        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            // 4 INNER diagonal pillars at radius 3 (close to core)
            Stone("Pillar_NE_in", new Vector2( 2.10f,  2.10f), 1.4f, STONE_GREY),
            Stone("Pillar_NW_in", new Vector2(-2.10f,  2.10f), 1.4f, STONE_GREY),
            Stone("Pillar_SE_in", new Vector2( 2.10f, -2.10f), 1.4f, STONE_GREY),
            Stone("Pillar_SW_in", new Vector2(-2.10f, -2.10f), 1.4f, STONE_GREY),
            // 4 OUTER diagonal pillars at radius 6
            Stone("Pillar_NE_out", new Vector2( 4.50f,  4.50f), 1.8f, STONE_GREY),
            Stone("Pillar_NW_out", new Vector2(-4.50f,  4.50f), 1.8f, STONE_GREY),
            Stone("Pillar_SE_out", new Vector2( 4.50f, -4.50f), 1.8f, STONE_GREY),
            Stone("Pillar_SW_out", new Vector2(-4.50f, -4.50f), 1.8f, STONE_GREY),
            // 4 short diagonal arches connecting outer pillars to perimeter.
            // Rotation rule: rotationDeg = position_angle + 90° (mod 360°),
            // so the convex side faces AWAY from the core.
            //   NE at (+7, +7) — angle  45°, rotation = 135°
            //   NW at (-7, +7) — angle 135°, rotation = 225° (or -135°)
            //   SE at (+7, -7) — angle -45° (315°), rotation =  45°
            //   SW at (-7, -7) — angle 225° (-135°), rotation = 315° (or -45°)
            Crescent("Arch_NE", pos: ( 7.00f,  7.00f), width: 2.4f, height: 1.0f, rotationDeg: 135f),
            Crescent("Arch_NW", pos: (-7.00f,  7.00f), width: 2.4f, height: 1.0f, rotationDeg: 225f),
            Crescent("Arch_SE", pos: ( 7.00f, -7.00f), width: 2.4f, height: 1.0f, rotationDeg: 45f),
            Crescent("Arch_SW", pos: (-7.00f, -7.00f), width: 2.4f, height: 1.0f, rotationDeg: 315f),
        };

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            // The open cardinal cross — the boss highways.
            ColorLine(false, new Vector2[] { new Vector2(0f, -9.5f), new Vector2(0f, 9.5f) }, LINE_WARM_AMBER),
            ColorLine(false, new Vector2[] { new Vector2(-9.5f, 0f), new Vector2(9.5f, 0f) }, LINE_WARM_AMBER),
        };
        return d;
    }

    // ----------------------------------------------------------------
    // 16. ASTEROID BELT — 6 round stones evenly spaced around a ring
    // ----------------------------------------------------------------
    // 6 stones at radius 5, spaced 60° apart with no rotation offset.
    // Centre-to-centre gap = 5 units. Stone diameter 2 → clear gap of
    // ~3 units between any two stones. Plenty for bosses.
    public static MapLayoutDefinition MakeAsteroidBelt()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Asteroid Belt";
        d.description = "Six round stones in a ring around the core. " +
                        "Generous gaps between every pair — six approach " +
                        "lanes for enemies.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            // Inner ring of slots, sitting INSIDE the asteroid ring (radius 2.5–3)
            // — positioned in the 6 gaps so they can fire outward through them.
            new Vector2(0.00f, 3.00f),   new Vector2(0.00f, -3.00f),
            new Vector2(2.60f, 1.50f),   new Vector2(-2.60f, 1.50f),
            new Vector2(2.60f, -1.50f),  new Vector2(-2.60f, -1.50f),
            // Outer ring of slots, behind the asteroids (radius 7) —
            // shoot at enemies that already passed the belt.
            new Vector2(0.00f, 8.00f),   new Vector2(0.00f, -8.00f),
            new Vector2(7.00f, 4.00f),   new Vector2(-7.00f, 4.00f),
            new Vector2(7.00f, -4.00f),  new Vector2(-7.00f, -4.00f),
            // 6 slots in the gaps themselves (between adjacent asteroids)
            new Vector2(4.30f, 0.00f),   new Vector2(-4.30f, 0.00f),
            new Vector2(2.20f, 3.80f),   new Vector2(-2.20f, 3.80f),
            new Vector2(2.20f, -3.80f),  new Vector2(-2.20f, -3.80f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(0.00f, 9.50f),   new Vector2(0.00f, -9.50f),
            new Vector2(9.50f, 0.00f),   new Vector2(-9.50f, 0.00f),
            // Bonus right at core
            new Vector2(1.50f, 0.00f),   new Vector2(-1.50f, 0.00f),
        };
        d.bonusSlotSize = 1.9f;

        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>();
        // 6 asteroids evenly spaced. First at top (angle 90°), then every 60°.
        float beltR = 5.0f;
        for (int i = 0; i < 6; i++)
        {
            float angDeg = 90f + i * 60f;
            float angRad = angDeg * Mathf.Deg2Rad;
            Vector2 pos = new Vector2(Mathf.Cos(angRad) * beltR, Mathf.Sin(angRad) * beltR);
            d.obstacles.Add(Stone($"Asteroid_{i}", pos, 2.0f, STONE_GREY));
        }

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            // The asteroid ring (orbit-style line)
            Line(true, ApproximateCircle(5.0f, 0f, 0f, 36)),
        };
        return d;
    }


    // ----------------------------------------------------------------
    // 18. PINWHEEL — curved spokes with stones along each spoke
    // ----------------------------------------------------------------
    // Unchanged from previous version. 4 spokes with 2 stones each at the
    // outer ends; wide open quadrants between spokes for enemy approach.
    public static MapLayoutDefinition MakePinwheel()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Pinwheel";
        d.description = "Four curved spokes pinwheel outward. Stones mark " +
                        "each spoke's outer end; wide open quadrants between.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            new Vector2(1.00f, 2.80f),   new Vector2(2.80f, 4.50f),   new Vector2(4.80f, 6.20f),
            new Vector2(2.80f, -1.00f),  new Vector2(4.50f, -2.80f),  new Vector2(6.20f, -4.80f),
            new Vector2(-1.00f, -2.80f), new Vector2(-2.80f, -4.50f), new Vector2(-4.80f, -6.20f),
            new Vector2(-2.80f, 1.00f),  new Vector2(-4.50f, 2.80f),  new Vector2(-6.20f, 4.80f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(6.50f, -6.50f),  new Vector2(-6.50f, 6.50f),
            new Vector2(7.50f, 7.50f),   new Vector2(-7.50f, -7.50f),
            new Vector2(0.00f, 8.50f),   new Vector2(0.00f, -8.50f),
            new Vector2(8.50f, 0.00f),   new Vector2(-8.50f, 0.00f),
        };
        d.bonusSlotSize = 1.9f;

        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            Stone("Spoke_NE_outer", new Vector2( 5.80f,  7.40f), 2.0f, STONE_GREY),
            Stone("Spoke_NE_mid",   new Vector2( 3.80f,  5.50f), 1.6f, STONE_GREY),
            Stone("Spoke_SE_outer", new Vector2( 7.40f, -5.80f), 2.0f, STONE_GREY),
            Stone("Spoke_SE_mid",   new Vector2( 5.50f, -3.80f), 1.6f, STONE_GREY),
            Stone("Spoke_SW_outer", new Vector2(-5.80f, -7.40f), 2.0f, STONE_GREY),
            Stone("Spoke_SW_mid",   new Vector2(-3.80f, -5.50f), 1.6f, STONE_GREY),
            Stone("Spoke_NW_outer", new Vector2(-7.40f,  5.80f), 2.0f, STONE_GREY),
            Stone("Spoke_NW_mid",   new Vector2(-5.50f,  3.80f), 1.6f, STONE_GREY),
        };

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            ColorLine(false, BuildQuadraticCurve(new Vector2( 0.5f,  2.5f), new Vector2( 1.5f,  5.0f), new Vector2( 5.5f,  7.0f), 16), LINE_WARM_AMBER),
            ColorLine(false, BuildQuadraticCurve(new Vector2( 2.5f, -0.5f), new Vector2( 5.0f, -1.5f), new Vector2( 7.0f, -5.5f), 16), LINE_WARM_AMBER),
            ColorLine(false, BuildQuadraticCurve(new Vector2(-0.5f, -2.5f), new Vector2(-1.5f, -5.0f), new Vector2(-5.5f, -7.0f), 16), LINE_WARM_AMBER),
            ColorLine(false, BuildQuadraticCurve(new Vector2(-2.5f,  0.5f), new Vector2(-5.0f,  1.5f), new Vector2(-7.0f,  5.5f), 16), LINE_WARM_AMBER),
        };
        return d;
    }

    // ================================================================
    // OBSTACLE HELPERS (used by the layouts above)
    // ================================================================

    // A round stone (Circle obstacle — single smooth CircleCollider2D).
    static MapLayoutDefinition.LayoutObstacle Stone(string label, Vector2 pos, float diameter, Color color)
    {
        return new MapLayoutDefinition.LayoutObstacle
        {
            shape = MapLayoutDefinition.ObstacleShape.Circle,
            position = pos,
            size = new Vector2(diameter, diameter),
            rotationDegrees = 0f,
            color = color,
            blocksMovement = true,
            label = label,
        };
    }

    // A curved arched-wall obstacle (Crescent — moon-shaped, with chained
    // circle colliders along the convex outer arc). The 'bite' (concave
    // side) faces the direction of rotationDeg (0°=up, 90°=left,
    // 180°=down, 270°=right). Put the convex side facing enemies so they
    // slide around it; never put the concave bite toward enemy approaches.
    static MapLayoutDefinition.LayoutObstacle Crescent(string label, (float x, float y) pos,
                                                       float width, float height, float rotationDeg)
    {
        return new MapLayoutDefinition.LayoutObstacle
        {
            shape = MapLayoutDefinition.ObstacleShape.Crescent,
            position = new Vector2(pos.x, pos.y),
            size = new Vector2(width, height),
            rotationDegrees = rotationDeg,
            color = Color.white, // texture provides color
            blocksMovement = true,
            label = label,
        };
    }

    // ================================================================
    // CONNECTION-LINE HELPERS (colored variants)
    // ================================================================

    static MapLayoutDefinition.ConnectionLine ColorLine(bool closed, Vector2[] points, Color color)
    {
        return new MapLayoutDefinition.ConnectionLine
        {
            closed = closed,
            color = color,
            width = 0.08f,
            points = new List<Vector2>(points),
        };
    }

    static MapLayoutDefinition.ConnectionLine ColorLine(bool closed, List<Vector2> points, Color color)
    {
        return new MapLayoutDefinition.ConnectionLine
        {
            closed = closed,
            color = color,
            width = 0.08f,
            points = new List<Vector2>(points),
        };
    }

    // ================================================================
    // GEOMETRY HELPERS — Quadratic Bezier for Pinwheel spokes
    // ================================================================

    static List<Vector2> BuildQuadraticCurve(Vector2 p0, Vector2 p1, Vector2 p2, int segments)
    {
        var pts = new List<Vector2>();
        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)(segments - 1);
            float u = 1f - t;
            Vector2 p = u * u * p0 + 2f * u * t * p1 + t * t * p2;
            pts.Add(new Vector2(
                Mathf.Round(p.x * 100f) / 100f,
                Mathf.Round(p.y * 100f) / 100f));
        }
        return pts;
    }
}

// ================================================================
// Lookup helper used by TowerDefenseMap's "Test Layout" override.
// Finds a built-in layout by display name (case-insensitive).
//
// Layouts returned are runtime-only ScriptableObjects, marked
// HideAndDontSave so the Unity inspector doesn't try to serialize them
// (avoids the 'IsInSyncWithParentSerializedObject' assertion that fires
// when a non-asset ScriptableObject gets selected/displayed).
// ================================================================
public static class MapLayoutExamplesLookup
{
    public static MapLayoutDefinition FindByName(string layoutName)
    {
        if (string.IsNullOrWhiteSpace(layoutName)) return null;
        foreach (var layout in MapLayoutExamples.CreateAll())
        {
            if (layout == null) continue;
            if (string.Equals(layout.layoutName, layoutName, System.StringComparison.OrdinalIgnoreCase))
            {
                // Mark as runtime-only so the Inspector doesn't track it.
                layout.hideFlags = HideFlags.HideAndDontSave;
                return layout;
            }
        }
        return null;
    }
}
