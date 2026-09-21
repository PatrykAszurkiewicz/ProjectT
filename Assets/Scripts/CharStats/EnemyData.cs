using UnityEngine;

[CreateAssetMenu(fileName = "EnemyData", menuName = "Enemies/EnemyData")]
public class EnemyData : ScriptableObject
{
    public string enemyName;
    public float maxHealth;
    public float maxArmor;
    public float moveSpeed;
    public float damage;

    [Header("Grappling Physics")]
    [Tooltip("Mass of the enemy in kilograms. Affects grappling hook behavior.")]
    public float mass = 50f;

    // ═══════════════════════════════════════════════════════════════════════
    //  SPRITE REFERENCES  (direct references replace the old Resources paths)
    // ═══════════════════════════════════════════════════════════════════════
    //
    //  WHY: the folder-path strings below feed Resources.LoadAll(). A string cannot
    //  be statically analysed, so Unity force-includes EVERYTHING under Resources/
    //  in the build and can never strip it. That is what made a sprite atlas a pure
    //  duplicate here (sources AND atlas pages both shipped and both loaded), and it
    //  is what forced the whole prewarm system to exist in the first place.
    //
    //  Direct Sprite[] references put the art back in the dependency graph:
    //      Prefab → EnemyStats → EnemyData → Sprite → Texture
    //  Unity then loads it during the scene load, off the main thread, and the atlas
    //  packer can finally strip the originals.
    //
    //  MEASUREMENT NOTE, so nobody chases the wrong thing again: profiling showed all
    //  21 sprite folders loading in 20 ms TOTAL (worst folder 2 ms). Loading was never
    //  the bottleneck. Do this refactor for the BUILD SIZE, the memory, and to make
    //  atlasing viable — not for boot speed.

    [Header("Animation — Sprite Frames (direct references, preferred)")]
    [Tooltip("Single-sequence frames, in order. When this has entries it is used and " +
             "Sprite Folder Path below is ignored entirely. Populate via " +
             "Tools ▸ Enemies ▸ Migrate Sprite Folders → Arrays.")]
    public Sprite[] frames;

    [Tooltip("Multi-folder mode: the looping movement/idle clip.")]
    public Sprite[] idleFrames;

    [Tooltip("Multi-folder mode: the attack clip. hitFrame and the parry frames are " +
             "indices INTO this array (0 = its first frame).")]
    public Sprite[] attackFrames;

    [Tooltip("Multi-folder mode: optional death clip. Leave EMPTY for instant death.")]
    public Sprite[] deathFrames;

    /// True when this asset has been migrated and no longer needs Resources at all.
    public bool HasDirectFrames =>
        (frames != null && frames.Length > 0) ||
        (idleFrames != null && idleFrames.Length > 0) ||
        (attackFrames != null && attackFrames.Length > 0);

    // ── LEGACY Resources paths ────────────────────────────────────────────────
    // Kept ONLY as a fallback for assets that have not been migrated yet, and as the
    // source of truth the migration tool reads from. Once every asset reports
    // HasDirectFrames == true, delete these four fields, delete the fallback branches
    // in EnemyAnimationController, and move the PNGs out of Resources/.

    [Header("Animation — LEGACY Resources paths (fallback only)")]
    [Tooltip("DEPRECATED. Used only when Frames above is empty. " +
             "e.g. \"Sprites/EnemySprites/Goblin\"")]
    public string spriteFolderPath;

    [Tooltip("Tick this ONLY if the source art is drawn facing LEFT instead of the " +
             "usual right. The flip system then mirrors it so the enemy faces its " +
             "movement / target correctly, and the walk-lean stays the right way " +
             "round. Leave OFF for right-facing art (every existing enemy).")]
    public bool spriteFacesLeft = false;

    [Tooltip("Default seconds-per-frame for all animations. " +
             "Each animation range can override this with its own speed.")]
    public float animationSpeed = 0.1f;

    [Header("Multi-Folder Animation (optional)")]
    [Tooltip("When ON, sprites are loaded from the separate per-animation folders " +
             "below INSTEAD of the single spriteFolderPath above. Each folder holds " +
             "its own 0-based frames (e.g. Move/00000..00007, Shoot/00000..00032), " +
             "so two clips can both start numbering at 00000 without colliding. " +
             "The idle/attack/death frame RANGES below are then filled in " +
             "AUTOMATICALLY from the folder contents — you only set hitFrame / parry " +
             "frames, which stay 0-based relative to the ATTACK (Shoot) folder.")]
    public bool useAnimationFolders = false;

    [Tooltip("DEPRECATED — used only when Idle Frames is empty. " +
             "Looping 'not attacking' animation (the walk / movement loop).")]
    public string idleFolderPath;

    [Tooltip("DEPRECATED — used only when Attack Frames is empty.")]
    public string attackFolderPath;

    [Tooltip("DEPRECATED — used only when Death Frames is empty.")]
    public string deathFolderPath;

    public AnimationFrameRange laserAttack = new AnimationFrameRange(57, 11);
    public AnimationFrameRange idle = new AnimationFrameRange(0, 14);
    public AnimationFrameRange attack = new AnimationFrameRange(14, 19);
    public AnimationFrameRange death = new AnimationFrameRange(33, 12);
    public float deathAnimationDuration = 1.2f; // How long before destroying the enemy

    // ATTACK FRAME EVENTS
    // All frame indices are RELATIVE to the attack animation (0 = first attack frame).
    // WORKFLOW — for each enemy / boss:
    //   1. Put your PNGs in Resources/Sprites/EnemySprites/YourEnemy/
    //      named 00.png, 01.png, 02.png, ... (alphabetically sortable).
    //   2. Set spriteFolderPath = "Sprites/EnemySprites/YourEnemy"
    //   3. Count your frames:
    //        idle   = startFrame: 0,  frameCount: 16  (sprites 00–15)
    //        attack = startFrame: 16, frameCount: 10  (sprites 16–25)
    //        death  = startFrame: 26, frameCount: 8   (sprites 26–33)
    //   4. Look at the attack sprites. Which one shows the weapon connecting?
    //        e.g. sprite 21 = frame index 5 relative to attack start → hitFrame = 5
    //   5. Which frames should be parryable? e.g. sprites 19–21 = frames 3–5
    //        → parryFrameStart = 3, parryFrameEnd = 5
    //   6. Optionally tune attack.speedOverride to control how fast the attack
    //        plays WITHOUT affecting idle/death speed.
    // If these are left at defaults (all -1), the system falls back to EnemyController's per-instance serialized fields for backward compatibility.


    [Header("Attack Frame Events (relative to attack start)")]

    [Tooltip("Frame (0-based, relative to attack.startFrame) where damage is dealt. " +
             "0 = instant damage at attack start (legacy behavior).")]
    public int hitFrame = 0;

    [Tooltip("First frame (0-based, relative to attack.startFrame) that opens the parry window.")]
    public int parryFrameStart = 0;

    [Tooltip("Last frame (0-based, inclusive, relative to attack.startFrame) that closes the parry window.")]
    public int parryFrameEnd = 0;


    /// Returns the effective seconds-per-frame for the given animation range.
    /// Uses the range's speedOverride if set, otherwise falls back to the global animationSpeed.

    public float GetAnimSpeed(AnimationFrameRange range)
    {
        return range.speedOverride > 0f ? range.speedOverride : animationSpeed;
    }


    /// Duration (in seconds) of one full attack animation cycle.

    public float AttackDuration => GetAnimSpeed(attack) * attack.frameCount;


    /// The effective seconds-per-frame for the attack animation.

    public float AttackAnimSpeed => GetAnimSpeed(attack);

    /// Time offset (in seconds) from attack start to hit frame.
    public float HitTimeOffset => AttackAnimSpeed * hitFrame;

    /// Time offset (in seconds) from attack start to parry window open.
    public float ParryStartTimeOffset => AttackAnimSpeed * parryFrameStart;

    /// Time offset (in seconds) from attack start to parry window close (end of last parry frame).
    public float ParryEndTimeOffset => AttackAnimSpeed * (parryFrameEnd + 1);

    /// Duration of the parry window in seconds.
    public float ParryWindowDuration => AttackAnimSpeed * (parryFrameEnd - parryFrameStart + 1);

#if UNITY_EDITOR
    // Validation happens at runtime in ResolveFrameConfig() instead of OnValidate,
    // because OnValidate fires on every keystroke and makes fields impossible to edit.
#endif
}

[System.Serializable]
public struct AnimationFrameRange
{
    public int startFrame;
    public int frameCount;

    [Tooltip("Seconds per frame for THIS animation only. " +
             "Leave at 0 to use the global animationSpeed. " +
             "Example: idle at 0.12s/frame, attack at 0.07s/frame for snappy hits.")]
    public float speedOverride;

    public AnimationFrameRange(int start, int count)
    {
        startFrame = start;
        frameCount = count;
        speedOverride = 0f;
    }
}


