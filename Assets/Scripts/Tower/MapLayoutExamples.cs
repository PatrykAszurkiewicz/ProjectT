// BUILT-IN LAYOUTS with slot positions, obstacles, and layout-shaped connection
// lines that visually link the slots.
//
// ============================================================================
//  SPACING CONTRACT — read this before editing any number in this file
// ============================================================================
//  Enemies steer locally (EnemyController) with no pathfinder, so the geometry
//  here IS the pathfinding. Every layout below is measured against four rules,
//  with an enemy roughly the size of a tower:
//
//      ENEMY radius 0.75      TOWER radius 0.75      CORE radius 1.0
//      LANE  = 2.00           the clear width an enemy needs to walk through
//      CLOSED = 0.50          a gap this narrow is a solid wall - an enemy
//                             can't get into it, so it can't trap them either
//
//   R1  obstacle <-> obstacle        gap >= LANE, or <= CLOSED (a merged wall)
//   R2  obstacle <-> slot + tower    gap >= LANE, or <= CLOSED
//   R3  slot + tower <-> slot + tower gap >= LANE, or <= CLOSED
//   R4  core <-> anything            gap >= LANE, or <= CLOSED
//   R5  with EVERY slot occupied by a tower, the core is still reachable
//       from outside the map.
//
//  In plain numbers that means:
//      slot centre to slot centre        >= 3.50
//      slot centre to obstacle surface   >= 2.75
//      obstacle surface to obstacle      >= 2.00
//      slot centre to map centre         >= 3.75
//
//  The dangerous zone is a gap BETWEEN those two thresholds - wide enough for
//  an enemy to nose into, too narrow to pass. That's the "stuck between the
//  obstacles" bug: the enemy wedges in, the avoidance push from both sides
//  cancels out, and it grinds there until stuck-mode kicks it loose. An
//  obstacle-slot-obstacle sandwich is the same trap with a tower as one wall,
//  which is why R2/R3 budget for a built tower rather than an empty slot.
//
//  R5 is what stops a player bricking the map: because no two towers can ever
//  be closer than a lane, and no tower can ever be a lane away from an
//  obstacle, there is no arrangement of towers that closes every route.
//
//  If you add or move anything here, re-check those four numbers by hand.
//
// Coordinate scale: the map radius is 10. Slot positions span roughly
// +-3.5 (inner) to +-12 (outer). Bonus slots can extend further.

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
            MakeBrokenCrown(),
            MakeTheFord(),
            MakeCrescentBastion(),
        };
    }

    // 01  CONCENTRIC CLASSIC  (rings draw themselves — no connection lines)
    // Ring radii are unchanged. Only the bonus ring moved out: at 12.50 it sat
    // 1.57 from the outer ring's towers, right in the trap zone.
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
        d.bonusSlotPositions = CirclePositions(14.60f, 8, 22.5f);
        d.bonusSlotSize = 1.9f;
        return d;
    }

    // 02  CHOKEPOINT CORRIDOR  (Custom + walls + horizontal guide lines)
    // Walls moved out to y=+-8.0 and the slot rows in to y=+-4.5, so a tower on
    // the row still leaves 2.05 between it and the wall (was 0.51 — a wedge).
    // The rows are evenly spaced at 3.50, the minimum tower-to-tower distance.
    public static MapLayoutDefinition MakeChokepointCorridor()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Chokepoint Corridor";
        d.description = "Walls funnel enemies through a horizontal lane.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            // Top row
            new Vector2(-7.00f, 4.50f), new Vector2(-3.50f, 4.50f), new Vector2(0.00f, 4.50f),
            new Vector2(3.50f, 4.50f), new Vector2(7.00f, 4.50f),
            // Bottom row
            new Vector2(-7.00f, -4.50f), new Vector2(-3.50f, -4.50f), new Vector2(0.00f, -4.50f),
            new Vector2(3.50f, -4.50f), new Vector2(7.00f, -4.50f),
            // Lane flanks
            new Vector2(-9.80f, 0.00f), new Vector2(9.80f, 0.00f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(-10.50f, 4.50f), new Vector2(10.50f, 4.50f),
            new Vector2(-10.50f, -4.50f), new Vector2(10.50f, -4.50f),
            new Vector2(-5.60f, 0.00f), new Vector2(5.60f, 0.00f),
        };
        d.bonusSlotSize = 1.9f;

        // Two rows of short wall segments with 2.10 gaps between them.
        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            Wall("TopWall_L", pos:(-5.60f, 8.00f), size:(3.50f, 1.40f)),
            Wall("TopWall_M", pos:(0.00f, 8.00f), size:(3.50f, 1.40f)),
            Wall("TopWall_R", pos:(5.60f, 8.00f), size:(3.50f, 1.40f)),
            Wall("BotWall_L", pos:(-5.60f, -8.00f), size:(3.50f, 1.40f)),
            Wall("BotWall_M", pos:(0.00f, -8.00f), size:(3.50f, 1.40f)),
            Wall("BotWall_R", pos:(5.60f, -8.00f), size:(3.50f, 1.40f)),
        };

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(closed:false, points: new Vector2[] {
                new Vector2(-11.20f, 4.50f), new Vector2(11.20f, 4.50f),
            }),
            Line(closed:false, points: new Vector2[] {
                new Vector2(-11.20f, -4.50f), new Vector2(11.20f, -4.50f),
            }),
        };
        return d;
    }

    // 03  SPIRAL SIEGE  (Custom + spiral guide line)
    // The pairs used to sit 2.80 apart across the path, so two towers left a
    // 1.30 gap — passable-looking, not passable. Pairs are now 3.80 apart and
    // the turns are spaced so no two slots on adjacent turns pinch either.
    public static MapLayoutDefinition MakeSpiralSiege()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Spiral Siege";
        d.description = "Slots line both edges of an inward spiral path.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        // 7 pairs, each straddling the spiral at +-1.90 from its centreline.
        d.customSlotPositions = new List<Vector2>
        {
            new Vector2(0.79f, 12.72f), new Vector2(-2.99f, 12.39f),
            new Vector2(-10.87f, 3.51f), new Vector2(-11.42f, -0.25f),
            new Vector2(-5.43f, -8.53f), new Vector2(-1.90f, -9.93f),
            new Vector2(5.95f, -6.49f), new Vector2(8.13f, -3.38f),
            new Vector2(6.69f, 3.41f), new Vector2(4.17f, 6.25f),
            new Vector2(-1.14f, 6.12f), new Vector2(-4.49f, 4.32f),
            new Vector2(-4.94f, 0.64f), new Vector2(-3.95f, -3.03f),
        };
        d.bonusSlotPositions = CirclePositions(14.50f, 6, 30f);
        d.bonusSlotSize = 1.9f;

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(closed:false, points: BuildSpiralPath(12.60f, 4.60f, 95f, 95f + 460f, 40)),
        };
        return d;
    }

    // 04  BREACHED FORTRESS  (Custom + walls + perimeter guide line)
    // The corners used to leave a 1.29 diagonal slot between the end of one wall
    // and the start of the next — the classic wedge. The wall spans now MEET at
    // the corners (gap 0), turning each corner into one solid L. All four
    // cardinal breaches are 11.90 wide, so the fortress is if anything easier to
    // enter than before; it just no longer has four traps built into it.
    public static MapLayoutDefinition MakeBreachedFortress()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Breached Fortress";
        d.description = "Perimeter wall with 4 cardinal gap breaches. " +
                        "Solid corners, wide openings.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        // Two concentric squares of slots inside the walls, plus a mid ring.
        d.customSlotPositions = new List<Vector2>();
        d.customSlotPositions.AddRange(CirclePositions(4.90f, 4, 0f));
        d.customSlotPositions.AddRange(CirclePositions(4.90f, 4, 45f));
        d.customSlotPositions.AddRange(CirclePositions(8.60f, 4, 0f));

        d.bonusSlotPositions = new List<Vector2>();
        d.bonusSlotPositions.AddRange(CirclePositions(8.60f, 4, 45f));
        d.bonusSlotPositions.AddRange(CirclePositions(12.20f, 4, 0f));
        d.bonusSlotSize = 1.9f;

        // Each corner is an L: the N/S spans and the E/W spans overlap exactly
        // at their ends, so there is no slot between them to get caught in.
        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            Wall("N_Wall_L", pos:(-8.16f, 10.92f), size:(4.41f, 1.12f)),
            Wall("N_Wall_R", pos:(8.16f, 10.92f), size:(4.41f, 1.12f)),
            Wall("S_Wall_L", pos:(-8.16f, -10.92f), size:(4.41f, 1.12f)),
            Wall("S_Wall_R", pos:(8.16f, -10.92f), size:(4.41f, 1.12f)),
            Wall("W_Wall_T", pos:(-10.92f, 8.16f), size:(1.12f, 4.41f)),
            Wall("W_Wall_B", pos:(-10.92f, -8.16f), size:(1.12f, 4.41f)),
            Wall("E_Wall_T", pos:(10.92f, 8.16f), size:(1.12f, 4.41f)),
            Wall("E_Wall_B", pos:(10.92f, -8.16f), size:(1.12f, 4.41f)),
        };

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(false, new Vector2[] { new Vector2(-10.92f, 10.92f), new Vector2(-5.95f, 10.92f) }),
            Line(false, new Vector2[] { new Vector2(5.95f, 10.92f), new Vector2(10.92f, 10.92f) }),
            Line(false, new Vector2[] { new Vector2(-10.92f, -10.92f), new Vector2(-5.95f, -10.92f) }),
            Line(false, new Vector2[] { new Vector2(5.95f, -10.92f), new Vector2(10.92f, -10.92f) }),
            Line(false, new Vector2[] { new Vector2(-10.92f, 10.92f), new Vector2(-10.92f, 5.95f) }),
            Line(false, new Vector2[] { new Vector2(-10.92f, -5.95f), new Vector2(-10.92f, -10.92f) }),
            Line(false, new Vector2[] { new Vector2(10.92f, 10.92f), new Vector2(10.92f, 5.95f) }),
            Line(false, new Vector2[] { new Vector2(10.92f, -5.95f), new Vector2(10.92f, -10.92f) }),
        };
        return d;
    }

    // 05  CROSSROADS  (Custom + lane grid guide lines)
    // Untouched — 7.00 slot spacing already clears every rule with room to spare.
    public static MapLayoutDefinition MakeCrossroads()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Crossroads";
        d.description = "Slots sit at the intersections of a lane grid. " +
                        "AoE towers shine.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            new Vector2(-7.00f, 7.00f), new Vector2(0.00f, 7.00f), new Vector2(7.00f, 7.00f),
            new Vector2(-7.00f, 0.00f),                            new Vector2(7.00f, 0.00f),
            new Vector2(-7.00f, -7.00f), new Vector2(0.00f, -7.00f), new Vector2(7.00f, -7.00f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(-11.20f, 11.20f), new Vector2(11.20f, 11.20f),
            new Vector2(-11.20f, -11.20f), new Vector2(11.20f, -11.20f),
            new Vector2(0.00f, 11.20f), new Vector2(0.00f, -11.20f),
            new Vector2(-11.20f, 0.00f), new Vector2(11.20f, 0.00f),
        };
        d.bonusSlotSize = 1.9f;

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(false, new Vector2[] { new Vector2(-11.90f, 7.00f), new Vector2(11.90f, 7.00f) }),
            Line(false, new Vector2[] { new Vector2(-11.90f, 0.00f), new Vector2(11.90f, 0.00f) }),
            Line(false, new Vector2[] { new Vector2(-11.90f, -7.00f), new Vector2(11.90f, -7.00f) }),
            Line(false, new Vector2[] { new Vector2(-7.00f, -11.90f), new Vector2(-7.00f, 11.90f) }),
            Line(false, new Vector2[] { new Vector2(0.00f, -11.90f), new Vector2(0.00f, 11.90f) }),
            Line(false, new Vector2[] { new Vector2(7.00f, -11.90f), new Vector2(7.00f, 11.90f) }),
        };
        return d;
    }

    // 06  THE GAUNTLET  (Custom + short deflector walls)
    // The runs were 4.20 apart with a divider dead centre, leaving 1.00 between a
    // tower and the wall. Runs are now 6.00 apart (y = 12 / 6 / 0) and the
    // dividers are thinner (0.50), which puts a clean 2.00 on both sides.
    public static MapLayoutDefinition MakeTheGauntlet()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "The Gauntlet";
        d.description = "Long zigzag path. Slots line each corridor run.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            // Run 1 (top)
            new Vector2(-9.80f, 12.00f), new Vector2(-4.20f, 12.00f),
            new Vector2(4.20f, 12.00f), new Vector2(9.80f, 12.00f),
            // Run 2
            new Vector2(9.80f, 6.00f), new Vector2(4.20f, 6.00f),
            new Vector2(-4.20f, 6.00f), new Vector2(-9.80f, 6.00f),
            // Run 3 (level with the core)
            new Vector2(-9.80f, 0.00f), new Vector2(9.80f, 0.00f),
            // Final approach
            new Vector2(-4.20f, -6.00f), new Vector2(4.20f, -6.00f),
            new Vector2(-4.20f, -10.00f), new Vector2(4.20f, -10.00f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(-13.30f, 9.00f), new Vector2(13.30f, 9.00f),
            new Vector2(-13.30f, 3.00f), new Vector2(14.00f, 3.00f),
            new Vector2(-11.90f, -8.00f), new Vector2(11.90f, -8.00f),
        };
        d.bonusSlotSize = 1.9f;

        // Divider rows sit exactly halfway between the runs, biased alternately
        // left and right so the open side flips each time.
        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            Wall("Div1_L", pos:(-8.40f, 9.00f), size:(3.50f, 0.50f)),
            Wall("Div1_M", pos:(-2.80f, 9.00f), size:(3.50f, 0.50f)),
            Wall("Div2_M", pos:(3.50f, 3.00f), size:(3.50f, 0.50f)),
            Wall("Div2_R", pos:(9.10f, 3.00f), size:(3.50f, 0.50f)),
        };

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(false, new Vector2[] {
                new Vector2(-11.90f, 12.00f), new Vector2(11.90f, 12.00f),
                new Vector2(11.90f, 6.00f),   new Vector2(-11.90f, 6.00f),
                new Vector2(-11.90f, 0.00f),  new Vector2(11.90f, 0.00f),
            }),
        };
        return d;
    }

    // 07  THE ARENA  (Custom — no physical obstacles, moat is visual only)
    // The island slots sat 3.65 from the map centre, leaving 1.90 between a
    // tower and the core. Island ring moved out to 4.20 and the outer ring
    // respaced so no two rings pinch.
    public static MapLayoutDefinition MakeTheArena()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "The Arena";
        d.description = "Core on an island behind a moat. Only 4 bridges cross.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>();
        d.customSlotPositions.AddRange(CirclePositions(4.20f, 4, 45f));   // island
        d.customSlotPositions.AddRange(CirclePositions(5.50f, 4, 0f));    // bridge guards
        d.customSlotPositions.AddRange(CirclePositions(11.50f, 8, 22.5f)); // outer ring

        d.bonusSlotPositions = CirclePositions(9.00f, 8, 0f);
        d.bonusSlotSize = 1.9f;

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(closed:true, points: ApproximateCircle(11.50f, 0.00f, 0.00f, 36)),
            Line(closed:true, points: ApproximateCircle(3.40f, 0.00f, 0.00f, 16)),
        };
        return d;
    }

    // 08  GHOST TOWN  (Custom + buildings)
    // Rebuilt. The old grid put buildings on the cardinal sides with 2.10 streets
    // between them, and slots at junctions where a tower closed the street down
    // to 1.23 — this was one of four layouts a player could brick outright.
    // Buildings now sit on the DIAGONALS and the four cardinal avenues are left
    // completely clear, so towers line the avenues without ever narrowing them.
    public static MapLayoutDefinition MakeGhostTown()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Ghost Town";
        d.description = "Buildings occupy the corners; four wide avenues run " +
                        "to the core. Slots line the avenues.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>();
        d.customSlotPositions.AddRange(CirclePositions(4.20f, 4, 0f));
        d.customSlotPositions.AddRange(CirclePositions(8.40f, 4, 0f));
        d.customSlotPositions.AddRange(CirclePositions(12.00f, 4, 0f));

        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(3.60f, 9.00f), new Vector2(-3.60f, 9.00f),
            new Vector2(3.60f, -9.00f), new Vector2(-3.60f, -9.00f),
            new Vector2(9.00f, 3.60f), new Vector2(-9.00f, 3.60f),
            new Vector2(9.00f, -3.60f), new Vector2(-9.00f, -3.60f),
        };
        d.bonusSlotSize = 1.9f;

        // Inner and outer blocks on each diagonal, 2.55 apart corner to corner.
        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            Building("NE_Block", pos:(9.60f, 9.60f), size:(4.20f, 4.20f)),
            Building("NW_Block", pos:(-9.60f, 9.60f), size:(4.20f, 4.20f)),
            Building("SE_Block", pos:(9.60f, -9.60f), size:(4.20f, 4.20f)),
            Building("SW_Block", pos:(-9.60f, -9.60f), size:(4.20f, 4.20f)),
            Building("Inner_NE", pos:(4.50f, 4.50f), size:(2.40f, 2.40f)),
            Building("Inner_NW", pos:(-4.50f, 4.50f), size:(2.40f, 2.40f)),
            Building("Inner_SE", pos:(4.50f, -4.50f), size:(2.40f, 2.40f)),
            Building("Inner_SW", pos:(-4.50f, -4.50f), size:(2.40f, 2.40f)),
        };

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            // The four avenues
            Line(false, new Vector2[] { new Vector2(-13.50f, 0.00f), new Vector2(13.50f, 0.00f) }),
            Line(false, new Vector2[] { new Vector2(0.00f, -13.50f), new Vector2(0.00f, 13.50f) }),
            // Ring road linking the avenue slots
            Line(true, new Vector2[] {
                new Vector2(8.40f, 0.00f), new Vector2(0.00f, 8.40f),
                new Vector2(-8.40f, 0.00f), new Vector2(0.00f, -8.40f),
            }),
        };
        return d;
    }

    // 09  MAZE HALLWAYS  (Custom + walls forming an H-pattern)
    // The corridors were 5.46 wide, so a tower parked in one left 1.98 on each
    // side — just under a lane, on both sides at once. Outer walls moved out to
    // +-10.50 (corridor 6.16 wide) and the slots centred at x = +-7.00, which
    // leaves 2.33 either side of a tower.
    public static MapLayoutDefinition MakeMazeHallways()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Maze Hallways";
        d.description = "Two vertical corridors connected by a horizontal one. " +
                        "Slots run down the middle of each corridor.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            // Left corridor (centred between the walls at -3.50 and -10.50)
            new Vector2(-7.00f, 12.00f), new Vector2(-7.00f, 8.40f), new Vector2(-7.00f, 4.50f),
            new Vector2(-7.00f, -4.50f), new Vector2(-7.00f, -8.40f), new Vector2(-7.00f, -12.00f),
            // Right corridor
            new Vector2(7.00f, 12.00f), new Vector2(7.00f, 8.40f), new Vector2(7.00f, 4.50f),
            new Vector2(7.00f, -4.50f), new Vector2(7.00f, -8.40f), new Vector2(7.00f, -12.00f),
            // Cross-corridor, kept clear of the core
            new Vector2(-2.80f, 2.80f), new Vector2(2.80f, 2.80f),
            new Vector2(-2.80f, -2.80f), new Vector2(2.80f, -2.80f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(0.00f, 6.30f), new Vector2(0.00f, -6.30f),
            new Vector2(0.00f, 10.50f), new Vector2(0.00f, -10.50f),
            new Vector2(13.00f, 0.00f), new Vector2(-13.00f, 0.00f),
        };
        d.bonusSlotSize = 1.9f;

        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            Wall("L_Out_T", pos:(-10.50f, 8.40f), size:(0.84f, 3.50f)),
            Wall("L_Out_B", pos:(-10.50f, -8.40f), size:(0.84f, 3.50f)),
            Wall("L_In_T",  pos:(-3.50f, 8.40f), size:(0.84f, 3.50f)),
            Wall("L_In_B",  pos:(-3.50f, -8.40f), size:(0.84f, 3.50f)),
            Wall("R_Out_T", pos:(10.50f, 8.40f), size:(0.84f, 3.50f)),
            Wall("R_Out_B", pos:(10.50f, -8.40f), size:(0.84f, 3.50f)),
            Wall("R_In_T",  pos:(3.50f, 8.40f), size:(0.84f, 3.50f)),
            Wall("R_In_B",  pos:(3.50f, -8.40f), size:(0.84f, 3.50f)),
        };

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(false, new Vector2[] { new Vector2(-10.50f, 13.00f), new Vector2(-10.50f, -13.00f) }),
            Line(false, new Vector2[] { new Vector2(-3.50f, 13.00f), new Vector2(-3.50f, -13.00f) }),
            Line(false, new Vector2[] { new Vector2(10.50f, 13.00f), new Vector2(10.50f, -13.00f) }),
            Line(false, new Vector2[] { new Vector2(3.50f, 13.00f), new Vector2(3.50f, -13.00f) }),
            Line(false, new Vector2[] { new Vector2(-3.50f, 0.00f), new Vector2(3.50f, 0.00f) }),
        };
        return d;
    }

    // 10  DIAMOND FORMATION  (Custom — no obstacles)
    // Untouched — every gap already clears the rules.
    public static MapLayoutDefinition MakeDiamondFormation()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Diamond Formation";
        d.description = "Slots arranged in a diamond grid (45° rotated). " +
                        "Diagonal coverage at every angle.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            new Vector2(4.90f, 0.00f), new Vector2(-4.90f, 0.00f),
            new Vector2(0.00f, 4.90f), new Vector2(0.00f, -4.90f),
            new Vector2(8.40f, 0.00f), new Vector2(-8.40f, 0.00f),
            new Vector2(0.00f, 8.40f), new Vector2(0.00f, -8.40f),
            new Vector2(5.88f, 5.88f), new Vector2(-5.88f, 5.88f),
            new Vector2(5.88f, -5.88f), new Vector2(-5.88f, -5.88f),
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

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(closed:true, points: new Vector2[] {
                new Vector2(4.90f, 0.00f), new Vector2(0.00f, 4.90f),
                new Vector2(-4.90f, 0.00f), new Vector2(0.00f, -4.90f),
            }),
            Line(closed:true, points: new Vector2[] {
                new Vector2(8.40f, 0.00f), new Vector2(0.00f, 8.40f),
                new Vector2(-8.40f, 0.00f), new Vector2(0.00f, -8.40f),
            }),
            Line(closed:true, points: new Vector2[] {
                new Vector2(11.90f, 0.00f), new Vector2(0.00f, 11.90f),
                new Vector2(-11.90f, 0.00f), new Vector2(0.00f, -11.90f),
            }),
        };
        return d;
    }

    // 11  PINCER GRIP  (Custom — two arc clusters, threat from top and bottom)
    // The arcs packed slots 2.20 apart in places (0.71 between towers). Rebuilt
    // as two clean arcs per flank: 4 slots on an outer arc at 8.50 and 3 on an
    // inner arc at 5.00, which keeps every neighbour at least 3.50 away.
    public static MapLayoutDefinition MakePincerGrip()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Pincer Grip";
        d.description = "Slots in two arc clusters, left and right of core. " +
                        "Top/bottom undefended — enemies enter from above and below.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>();
        foreach (float baseAngle in new float[] { 0f, 180f })
        {
            foreach (float a in new float[] { -50f, -16.7f, 16.7f, 50f })
                d.customSlotPositions.Add(Polar(8.50f, baseAngle + a));
            foreach (float a in new float[] { -45f, 0f, 45f })
                d.customSlotPositions.Add(Polar(5.00f, baseAngle + a));
        }

        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(0.00f, 11.00f), new Vector2(0.00f, -11.00f),
            new Vector2(3.50f, 9.50f), new Vector2(-3.50f, 9.50f),
            new Vector2(3.50f, -9.50f), new Vector2(-3.50f, -9.50f),
        };
        d.bonusSlotSize = 1.9f;

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(false, points: BuildArc(0.00f, 0.00f, 8.50f, 130f, 230f, 16)),
            Line(false, points: BuildArc(0.00f, 0.00f, 8.50f, -50f, 50f, 16)),
        };
        return d;
    }

    // ================================================================
    // ROUND / CURVED LAYOUTS (Circle and Crescent obstacles)
    // ================================================================

    static readonly Color STONE_GREY = new Color(0.45f, 0.46f, 0.50f, 1.00f);
    static readonly Color LINE_WARM_AMBER = new Color(0.90f, 0.65f, 0.30f, 0.55f);

    // 12  STONEHENGE — 8 outer stones + 4 inner stones
    // The inner stones sat 3.50 out, 1.75 from the core — an enemy could get
    // between them and the core but not back out. And the inner slot ring was
    // pinched 1.06 between two outer stones, so towers there sealed the map.
    // Both rings moved out: stones to 8.50 / 4.20, slots to 11.00 / 5.30.
    public static MapLayoutDefinition MakeStonehenge()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Stonehenge";
        d.description = "Eight large standing stones in an outer ring with " +
                        "four inner stones on the diagonals. Wide cardinal " +
                        "gaps for boss access.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>();
        d.customSlotPositions.AddRange(CirclePositions(11.00f, 8, 0f));  // outside the ring
        d.customSlotPositions.AddRange(CirclePositions(5.30f, 4, 0f));   // inside, on the cardinals

        d.bonusSlotPositions = CirclePositions(13.00f, 8, 22.5f);
        d.bonusSlotSize = 1.9f;

        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>();
        // OUTER ring: 8 stones on the diagonals-between-cardinals, 4.31 gaps.
        for (int i = 0; i < 8; i++)
            d.obstacles.Add(Stone($"OuterStone_{i}", Polar(8.50f, i * 45f + 22.5f), 2.2f, STONE_GREY));
        // INNER ring: 4 stones on the diagonals, 2.45 clear of the core.
        for (int i = 0; i < 4; i++)
            d.obstacles.Add(Stone($"InnerStone_{i}", Polar(4.20f, 45f + i * 90f), 1.5f, STONE_GREY));

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(true, ApproximateCircle(8.5f, 0f, 0f, 36)),
            Line(true, ApproximateCircle(4.2f, 0f, 0f, 24)),
        };
        return d;
    }

    // 13  CROSSROADS PILLARS — open cardinal cross, pillars on the diagonals
    // The inner pillar ring sat at radius 2.97, and the cardinal slots at 3.00
    // ran right between them: four towers plus four pillars formed a closed ring
    // around the core, sealing the map completely. The inner ring is gone; the
    // pillars now sit at 6.36 on the diagonals with the cardinal highways
    // completely clear, and the outer diagonals carry solid arches (real
    // colliders — enemies path around them, not through them).
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
            new Vector2(0.00f, 4.50f), new Vector2(0.00f, -4.50f),
            new Vector2(4.50f, 0.00f), new Vector2(-4.50f, 0.00f),
            new Vector2(0.00f, 8.00f), new Vector2(0.00f, -8.00f),
            new Vector2(8.00f, 0.00f), new Vector2(-8.00f, 0.00f),
            new Vector2(0.00f, 11.50f), new Vector2(0.00f, -11.50f),
            new Vector2(11.50f, 0.00f), new Vector2(-11.50f, 0.00f),
        };
        d.bonusSlotPositions = new List<Vector2>
        {
            new Vector2(3.60f, 8.20f), new Vector2(-3.60f, 8.20f),
            new Vector2(3.60f, -8.20f), new Vector2(-3.60f, -8.20f),
            new Vector2(8.20f, 3.60f), new Vector2(-8.20f, 3.60f),
            new Vector2(8.20f, -3.60f), new Vector2(-8.20f, -3.60f),
        };
        d.bonusSlotSize = 1.9f;

        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            Stone("Pillar_NE", new Vector2( 4.50f,  4.50f), 1.8f, STONE_GREY),
            Stone("Pillar_NW", new Vector2(-4.50f,  4.50f), 1.8f, STONE_GREY),
            Stone("Pillar_SE", new Vector2( 4.50f, -4.50f), 1.8f, STONE_GREY),
            Stone("Pillar_SW", new Vector2(-4.50f, -4.50f), 1.8f, STONE_GREY),

            // Solid arches on the outer diagonals — real colliders, enemies
            // cannot pass through them. Each one's open side faces the core, so
            // enemies coming inward meet the convex back and slide around it.
            // 3.60 x 1.00 keeps the arc shallow (see the crescent sizing note at
            // the bottom of this file); the nearest a tower can ever get is 2.97.
            Arch("Arch_NE", new Vector2( 8.50f,  8.50f), 3.6f, 1.0f, 135f),
            Arch("Arch_NW", new Vector2(-8.50f,  8.50f), 3.6f, 1.0f, 225f),
            Arch("Arch_SE", new Vector2( 8.50f, -8.50f), 3.6f, 1.0f, 45f),
            Arch("Arch_SW", new Vector2(-8.50f, -8.50f), 3.6f, 1.0f, 315f),
        };

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            ColorLine(false, new Vector2[] { new Vector2(0f, -12.5f), new Vector2(0f, 12.5f) }, LINE_WARM_AMBER),
            ColorLine(false, new Vector2[] { new Vector2(-12.5f, 0f), new Vector2(12.5f, 0f) }, LINE_WARM_AMBER),
        };
        return d;
    }

    // 14  ASTEROID BELT — 6 round stones evenly spaced around a ring
    // The belt sat at radius 5.00, and the old layout deliberately put slots
    // "in the gaps themselves" — six towers plugging six holes sealed the map.
    // Worse, the geometry made an inner slot impossible: with stones at 5.00 no
    // point between the core and the belt is 3.75 clear of a stone centre. The
    // belt moved out to 7.00, which opens a genuine inner courtyard: slots ride
    // at 4.50 inside and 9.50 outside, both in the angular gaps, and neither can
    // block the 5.00-wide lanes between the stones.
    public static MapLayoutDefinition MakeAsteroidBelt()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Asteroid Belt";
        d.description = "Six round stones in a ring around the core. " +
                        "Generous gaps between every pair — six approach " +
                        "lanes for enemies.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        // Both slot rings sit at 60° offsets — i.e. lined up with the gaps
        // between stones, never behind a stone.
        d.customSlotPositions = new List<Vector2>();
        d.customSlotPositions.AddRange(CirclePositions(4.50f, 6, 60f));
        d.customSlotPositions.AddRange(CirclePositions(9.50f, 6, 60f));

        d.bonusSlotPositions = CirclePositions(13.40f, 6, 60f);
        d.bonusSlotSize = 1.9f;

        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>();
        for (int i = 0; i < 6; i++)
            d.obstacles.Add(Stone($"Asteroid_{i}", Polar(7.00f, 90f + i * 60f), 2.0f, STONE_GREY));

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(true, ApproximateCircle(7.0f, 0f, 0f, 36)),
        };
        return d;
    }

    // 15  PINWHEEL — curved spokes with stones along each spoke
    // The two stones on each spoke were 0.96 apart (a wedge), the spoke slots
    // were 2.47 apart (0.98 between towers), and the innermost slot was 1.22
    // from the core. Slots respaced to 3.60 along each arm starting at radius
    // 3.96.
    //
    // The two stones per arm used to sit 1.48 apart with radii summing to 1.80 —
    // an 18% PARTIAL overlap. That is the worst of both options: too much to read
    // as two separate rocks, too little to read as one blob, so it just looked
    // like a rendering mistake. Each also keeps its own CircleCollider2D, so the
    // overlap region is double-covered and the concave notch where the two circles
    // meet is exactly the kind of crease pathing snags on.
    //
    // They are now spaced to a clean 0.35 gap: two distinct rocks with daylight
    // between them, and two convex colliders enemies slide around.
    public static MapLayoutDefinition MakePinwheel()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Pinwheel";
        d.description = "Four curved spokes pinwheel outward. Stone blobs flank " +
                        "each spoke; wide open quadrants between.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        // One arm, repeated at 90° intervals.
        Vector2[] arm = { new Vector2(1.40f, 3.70f), new Vector2(3.80f, 6.40f), new Vector2(6.20f, 9.10f) };
        d.customSlotPositions = new List<Vector2>();
        for (int turn = 0; turn < 4; turn++)
            foreach (var p in arm) d.customSlotPositions.Add(Rotate90(p, turn));

        d.bonusSlotPositions = CirclePositions(13.00f, 8, 30f);
        d.bonusSlotSize = 1.9f;

        // Two SEPARATED stones per arm. Centres 2.15 apart against radii summing
        // to 1.80, i.e. a 0.35 clear gap — see the note above.
        (Vector2 pos, float dia)[] armStones =
        {
            (new Vector2(6.80f, 3.76f), 2.0f),
            (new Vector2(7.97f, 5.57f), 1.6f),
        };
        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>();
        for (int turn = 0; turn < 4; turn++)
            for (int s = 0; s < armStones.Length; s++)
                d.obstacles.Add(Stone($"Spoke_{turn}_{s}", Rotate90(armStones[s].pos, turn),
                                      armStones[s].dia, STONE_GREY));

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>();
        for (int turn = 0; turn < 4; turn++)
        {
            d.connectionLines.Add(ColorLine(false, BuildQuadraticCurve(
                Rotate90(new Vector2(0.80f, 3.20f), turn),
                Rotate90(new Vector2(2.20f, 5.60f), turn),
                Rotate90(new Vector2(6.60f, 9.60f), turn), 16), LINE_WARM_AMBER));
        }
        return d;
    }


    // ================================================================
    // ASYMMETRIC LAYOUTS
    // The core sits at the origin in every layout, so asymmetry comes from
    // where the slots and terrain are NOT. Each of these leaves one flank of
    // the core genuinely open: a wide arc with no slots and no obstacles, so
    // enemies arriving from that side reach the core with far less resistance
    // than from any other bearing.
    //
    // R5 is trivially satisfied by all three — the open flank is a permanent
    // corridor that no arrangement of towers can close.
    // ================================================================

    // 16  BROKEN CROWN  (open NORTH)
    // Concentric Classic with a 90 degree bite taken out of the north. The
    // inner ring keeps 7 of its 8 positions (the one at 90 is gone) and the
    // outer ring runs 130 -> 410, leaving 50..130 empty.
    //
    // Two stumps sit INSIDE the gap rather than at its edges. At the edges they
    // would land 0.37 from the nearest outer slot — a wedge. At 75 and 105 they
    // are 2.90 clear of the nearest tower and 2.66 from each other, so the
    // breach reads as collapsed masonry without ever pinching.
    public static MapLayoutDefinition MakeBrokenCrown()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Broken Crown";
        d.description = "Concentric rings with the northern quarter collapsed. " +
                        "Every other bearing is fully defended; the north is a " +
                        "90 degree hole straight to the core.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>();
        // Inner ring: 45 degree spacing (chord 3.83), skipping due north.
        for (int i = 0; i < 7; i++)
            d.customSlotPositions.Add(Polar(5.00f, 135f + i * 45f));
        // Outer ring: 28 degree spacing (chord 4.35), 130 -> 410.
        for (int i = 0; i < 11; i++)
            d.customSlotPositions.Add(Polar(9.00f, 130f + i * 28f));

        d.bonusSlotPositions = new List<Vector2>();
        for (int i = 0; i < 9; i++)
            d.bonusSlotPositions.Add(Polar(12.50f, 140f + i * 32.5f));
        d.bonusSlotSize = 1.9f;

        // The two surviving stumps of the collapsed arc.
        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            Stone("Stump_E", Polar(9.00f, 75f), 2.0f, STONE_GREY),
            Stone("Stump_W", Polar(9.00f, 105f), 2.0f, STONE_GREY),
        };

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            Line(false, BuildArc(0f, 0f, 9.00f, 130f, 410f, 40)),
            Line(false, BuildArc(0f, 0f, 5.00f, 135f, 405f, 28)),
        };
        return d;
    }

    // 17  THE FORD  (open NORTH-EAST)
    // A line of boulders lies across the south-west on the chord 9.00 out along
    // the 225 bearing. It is deliberately SPARSE: four rocks, three lanes.
    //
    //   outer gaps 2.19   inner gap (the ford) 3.21   both ends fully open
    //
    // Every gap clears LANE, so nothing here can wedge an enemy — this is a
    // shallow river to wade, not a wall. The rocks shape the approach rather
    // than blocking it, and the slots watch the crossings from behind.
    //
    // The whole north-east, from bearing 320 round to 130, has no slots and no
    // terrain at all: a 170 degree open field. That is the layout's point.
    public static MapLayoutDefinition MakeTheFord()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "The Ford";
        d.description = "A sparse boulder line fords the south-west with three " +
                        "wadeable lanes. Slots watch the crossings; the entire " +
                        "north-east is open field.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        d.customSlotPositions = new List<Vector2>
        {
            // Crossing guards: 45 degree spacing at 5.00 (chord 3.83).
            Polar(5.00f, 180f), Polar(5.00f, 225f), Polar(5.00f, 270f),
            // Bank arcs, held clear of the boulder line (nearest rock 5.93).
            Polar(9.00f, 130f), Polar(9.00f, 155f),
            Polar(9.00f, 295f), Polar(9.00f, 320f),
        };

        d.bonusSlotPositions = new List<Vector2>
        {
            // Forward posts out on the exposed side.
            Polar(12.50f, 0f), Polar(12.50f, 45f),
            Polar(12.50f, 90f), Polar(12.50f, 120f),
            new Vector2(4.50f, 0.00f),
        };
        d.bonusSlotSize = 1.9f;

        // Four rocks on the line through (-13.44, 0.71) -> (0.71, -13.44).
        // Diameter 2.8, so surface gaps are 2.19 / 3.21 / 2.19 from west to east.
        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>
        {
            Stone("Ford_Rock_0", new Vector2(-12.02f, -0.71f), 2.8f, STONE_GREY),
            Stone("Ford_Rock_1", new Vector2(-8.49f, -4.24f), 2.8f, STONE_GREY),
            Stone("Ford_Rock_2", new Vector2(-4.24f, -8.49f), 2.8f, STONE_GREY),
            Stone("Ford_Rock_3", new Vector2(-0.71f, -12.02f), 2.8f, STONE_GREY),
        };

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            // The riverbed itself.
            ColorLine(false, new Vector2[] {
                new Vector2(-13.44f, 0.71f), new Vector2(0.71f, -13.44f),
            }, LINE_WARM_AMBER),
            Line(false, BuildArc(0f, 0f, 5.00f, 180f, 270f, 16)),
            Line(false, BuildArc(0f, 0f, 9.00f, 130f, 155f, 10)),
            Line(false, BuildArc(0f, 0f, 9.00f, 295f, 320f, 10)),
        };
        return d;
    }

    // 18  CRESCENT BASTION  (open SOUTH-WEST)
    // Three crescent arches curve around the core's north-east at radius 9.50,
    // spaced 50 degrees apart so the gaps between them are 4.46 — more than two
    // lanes wide. Enemies walk straight between the arches; the bastion shapes
    // the approach and gives the towers something to shoot across, it does not
    // seal anything.
    //
    // Each arch is 3.20 x 1.00. The minor axis is under the 1.20 ceiling in the
    // Arch() sizing note, so the bowl is shallower than an enemy is wide and
    // none of them can be nosed into. Openings face the core (rotation = bearing
    // + 90), so anything coming inward meets the convex back and slides off.
    //
    // Slots stack in front of and behind each arch. The south-west quadrant is
    // left completely bare — 8 slots total makes this the sparsest layout in
    // the file, and the hardest.
    public static MapLayoutDefinition MakeCrescentBastion()
    {
        var d = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        d.layoutName = "Crescent Bastion";
        d.description = "Three widely spaced crescent arches shield the " +
                        "north-east. Enemies pass freely between them, and the " +
                        "south-west quadrant is undefended ground.";
        d.layoutType = MapLayoutDefinition.LayoutType.Custom;
        d.customSlotSize = 1.9f;

        // Bearings the bastion is built on.
        float[] bastionAngles = { 20f, 70f, 120f };

        d.customSlotPositions = new List<Vector2>();
        // Behind the wall (3.17 clear of the collider chain).
        foreach (float a in bastionAngles) d.customSlotPositions.Add(Polar(6.00f, a));
        // In front of it (3.17 clear on the outside).
        foreach (float a in bastionAngles) d.customSlotPositions.Add(Polar(13.00f, a));
        // Two wings covering the flanks of the open quadrant.
        d.customSlotPositions.Add(Polar(7.50f, 160f));
        d.customSlotPositions.Add(Polar(7.50f, 335f));

        d.bonusSlotPositions = new List<Vector2>
        {
            Polar(5.00f, 245f),                       // last-ditch post facing the gap
            Polar(9.50f, 200f), Polar(9.50f, 290f),   // shoulders of the open quadrant
            Polar(12.00f, 160f),
        };
        d.bonusSlotSize = 1.9f;

        d.obstacles = new List<MapLayoutDefinition.LayoutObstacle>();
        foreach (float a in bastionAngles)
            d.obstacles.Add(Arch($"Bastion_{a:F0}", Polar(9.50f, a), 3.2f, 1.0f, a + 90f));

        d.connectionLines = new List<MapLayoutDefinition.ConnectionLine>
        {
            ColorLine(false, BuildArc(0f, 0f, 9.50f, 20f, 120f, 24), LINE_WARM_AMBER),
        };
        return d;
    }

    // ================================================================
    // OBSTACLE HELPERS
    // ================================================================

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

    // A curved arched wall (Crescent obstacle) with a REAL collider — enemies
    // cannot walk through it. Unity gets a chain of overlapping CircleCollider2D
    // along the rim, so the surface is smooth and enemies slide around it.
    //
    // SIZING RULE — read before adding one anywhere
    // A crescent is a C, not a banana: the rim covers about 267°, leaving an
    // opening of roughly 93°. Whether that opening is dangerous depends entirely
    // on the MINOR axis:
    //
    //   height <= 1.20  the bowl is shallower than an enemy is wide, so an enemy
    //                   can't get inside it at all. It behaves as a solid curved
    //                   wall. SAFE — this is what the layouts here use.
    //   height >  1.40  the bowl becomes an alcove an enemy can nose into and
    //                   then has to reverse out of. Local steering handles that
    //                   badly: it wanders in, grinds against the back, and looks
    //                   stuck. DON'T.
    //
    // At 3.60 x 1.00 the collider chain is 22 circles of radius 0.22, reaching
    // 1.80 along the arc and 0.66 through the thickness. Budget clearance from
    // those extents, not from the centre.
    //
    // Point the opening at the core (rotationDeg = the direction the open side
    // faces, minus 90) so enemies coming inward hit the convex back first.
    static MapLayoutDefinition.LayoutObstacle Arch(string label, Vector2 pos,
                                                   float width, float height, float rotationDeg)
    {
        return new MapLayoutDefinition.LayoutObstacle
        {
            shape = MapLayoutDefinition.ObstacleShape.Crescent,
            position = pos,
            size = new Vector2(width, height),
            rotationDegrees = rotationDeg,
            color = STONE_GREY,
            blocksMovement = true,
            label = label,
        };
    }

    // ================================================================
    // CONNECTION-LINE HELPERS
    // ================================================================

    static MapLayoutDefinition.ConnectionLine Line(bool closed, Vector2[] points)
    {
        return new MapLayoutDefinition.ConnectionLine
        {
            closed = closed,
            color = LINE_COLOR,
            width = 0.08f,
            points = new List<Vector2>(points),
        };
    }

    static MapLayoutDefinition.ConnectionLine Line(bool closed, List<Vector2> points)
    {
        return new MapLayoutDefinition.ConnectionLine
        {
            closed = closed,
            color = LINE_COLOR,
            width = 0.08f,
            points = new List<Vector2>(points),
        };
    }

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
    // GEOMETRY HELPERS
    // ================================================================

    // Point at a polar coordinate, rounded to 2dp to match the authored style.
    static Vector2 Polar(float radius, float angleDeg)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        return new Vector2(
            Mathf.Round(radius * Mathf.Cos(a) * 100f) / 100f,
            Mathf.Round(radius * Mathf.Sin(a) * 100f) / 100f);
    }

    // Rotates a point by -90° per turn (used to repeat one pinwheel arm).
    static Vector2 Rotate90(Vector2 p, int turns)
    {
        for (int i = 0; i < turns; i++) p = new Vector2(p.y, -p.x);
        return p;
    }

    static List<Vector2> ApproximateCircle(float radius, float cx, float cy, int segments)
    {
        // Auto-bump under-sampled circles so they render as smooth curves
        // rather than visible polygons at typical 2D camera distances.
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

    static List<Vector2> CirclePositions(float radius, int count, float offsetDeg)
    {
        var list = new List<Vector2>(count);
        for (int i = 0; i < count; i++)
            list.Add(Polar(radius, i * 360f / count + offsetDeg));
        return list;
    }

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
                layout.hideFlags = HideFlags.HideAndDontSave;
                return layout;
            }
        }
        return null;
    }
}


