using UnityEngine;

// Controller for the Parfumer enemy.
//   Chases only the player (falls back to the core if there is no player, so it
//   never idles and soft-locks the wave — same safeguard the Buffer uses).
//   On a fixed cadence, drops a lingering greenish PoisonCloud at its current
//   position. 
[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(Rigidbody2D))]
public class ParfumerController : MonoBehaviour
{
    [Header("Targeting")]
    [Tooltip("How close (world units) the Parfumer wants to be to the player " +
             "before stopping. Keeps it from grinding into the player's " +
             "collider while still parking inside its own cloud radius.")]
    [SerializeField] private float stoppingDistance = 1.2f;

    [Tooltip("If true, when no player exists the Parfumer falls back to walking " +
             "toward the core. Prevents it from idling in place — which would " +
             "soft-lock the wave, since it deals no direct damage. " +
             "Strongly recommended ON.")]
    [SerializeField] private bool fallbackToCoreIfNoPlayer = true;

    [Header("Poison Cloud")]
    [Tooltip("Prefab spawned as the lingering poison patch. If left null, a " +
             "GameObject is created procedurally at runtime (with " +
             "BufferFogVisual + PoisonCloud attached).")]
    [SerializeField] private GameObject cloudPrefab;

    [Tooltip("Seconds between cloud drops.")]
    [SerializeField] private float cloudDropInterval = 2.0f;

    [Tooltip("How long each spawned cloud patch lingers.")]
    [SerializeField] private float cloudDuration = 5f;

    [Tooltip("Radius of each spawned cloud patch.")]
    [SerializeField] private float cloudRadius = 2.5f;

    [Tooltip("Seconds the poison keeps ticking on the player AFTER exposure. " +
             "The defining Parfumer trait — default 20.")]
    [SerializeField] private float poisonDuration = 20f;

    [Tooltip("Damage per second dealt to the player while the poison is active.")]
    [SerializeField] private float poisonDamagePerSecond = 6f;

    [Header("Cloud Visual")]
    [Tooltip("Toggle the soft green mist body.")]
    [SerializeField] private bool cloudEnableMist = true;

    [Tooltip("Toggle the curling pale tendrils sprouting from the cloud.")]
    [SerializeField] private bool cloudEnableWisps = true;

    [Tooltip("Mist body color. Alpha drives additive intensity, not transparency.")]
    [SerializeField] private Color cloudMistColor = new Color(0.25f, 0.65f, 0.15f, 0.30f);

    [Tooltip("Wisp / tendril color. A pale toxic green reads against the mist.")]
    [SerializeField] private Color cloudWispColor = new Color(0.55f, 0.95f, 0.40f, 0.55f);

    [Tooltip("Transparent edge color for mist falloff. Keep alpha 0.")]
    [SerializeField] private Color cloudOuterColor = new Color(0.10f, 0.30f, 0.05f, 0f);

    [Header("Death VFX")]
    [Tooltip("Duration of the disintegration VFX played when the Parfumer dies. " +
             "Set at runtime via EnemyStats.ConfigureDeathVfx(). Below 1.0 = " +
             "'classic chunks'; 1.0+ = boss-style sprite-shatter. 0 disables.")]
    [SerializeField] private float deathVfxDuration = 0.7f;

    // Cached references
    private EnemyStats stats;
    private Rigidbody2D rb;
    private Transform playerTransform;
    private Transform coreTransform;

    private float cloudDropTimer;
    private Transform currentTarget;
    private float smokeShufflePhase;

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
        rb = GetComponent<Rigidbody2D>();

        // Auto-add Y-sort entity (EnemyController normally does this; we replace
        // it, so we take over the job — same values the Buffer uses).
        if (GetComponent<YSortEntity>() == null)
        {
            var ysort = gameObject.AddComponent<YSortEntity>();
            ysort.sortPrecision = 10f;
            ysort.sortOrderBase = 1000;
            ysort.sortYOffset = -0.2f;
        }

        // The Parfumer never melees — its damage is purely the cloud
        var animController = GetComponent<EnemyAnimationController>();
        if (animController != null)
            animController.SetAutoAttackDetectionEnabled(false);

        // Configure the death VFX on the EnemyStats so we don't depend on the
        // prefab inspector having the right value.
        if (stats != null && deathVfxDuration > 0f)
        {
            stats.ConfigureDeathVfx(deathVfxDuration, destroyHealthBarBeforeVfx: true);
        }

        // Stagger the first drop so a group of Parfumers doesn't fire their
        // initial clouds all on the same frame.
        cloudDropTimer = Random.Range(0f, cloudDropInterval * 0.5f);
        smokeShufflePhase = SmokeBlind.NewPhase();
    }

    private void Update()
    {
        if (stats == null || stats.IsDead()) return;

        UpdateTarget();

        cloudDropTimer += Time.deltaTime;
        if (cloudDropTimer >= cloudDropInterval)
        {
            cloudDropTimer = 0f;
            SpawnCloud();
        }
    }

    private void FixedUpdate()
    {
        if (stats == null || stats.IsDead() || rb == null) return;

        if (currentTarget == null)
        {
            // No target — hold position. Killing residual velocity keeps the
            // Parfumer from drifting after a nudge.
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Smoke Screen: if a smoke cloud blocks our sightline to the target, we
        // lose sight of it and mill in place until the smoke clears — same as
        // every other enemy.
        if (SmokeBlind.Blocks(transform.position, currentTarget.position))
        {
            rb.linearVelocity = SmokeBlind.ShuffleVelocity(smokeShufflePhase, stats.MoveSpeed);
            return;
        }

        Vector2 toTarget = (Vector2)currentTarget.position - rb.position;
        float dist = toTarget.magnitude;

        if (dist <= stoppingDistance)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 dir = toTarget / dist; // normalized without re-sqrt
        rb.linearVelocity = dir * stats.MoveSpeed;
    }

    private void UpdateTarget()
    {
        // Co-op: chase the nearest alive player. Resolving every frame means the
        // Parfumer retargets automatically when a player goes down. includeCloaked
        // is true to preserve the Parfumer's original cloak-agnostic chasing.
        // With one player this is identical to the old single lookup.
        var nearest = PlayerRegistry.Instance.NearestAlive(transform.position, includeCloaked: true);
        if (nearest != null)
        {
            playerTransform = nearest.transform;
            currentTarget = playerTransform;
            return;
        }
        playerTransform = null;

        // Fallback to the core so we never idle and soft-lock the wave.
        if (fallbackToCoreIfNoPlayer)
        {
            if (coreTransform == null)
            {
                GameObject coreGO = GameObject.FindGameObjectWithTag("Core");
                coreTransform = coreGO != null ? coreGO.transform : null;
            }
            currentTarget = coreTransform;
        }
        else
        {
            currentTarget = null;
        }
    }

    private void SpawnCloud()
    {
        GameObject cloudGO;
        if (cloudPrefab != null)
        {
            cloudGO = Instantiate(cloudPrefab, transform.position, Quaternion.identity);
        }
        else
        {
            // Procedural fallback: build the cloud from scratch so the designer
            // doesn't need to wire a prefab to get the enemy working.
            cloudGO = new GameObject("PoisonCloud");
            cloudGO.transform.position = transform.position;
            cloudGO.AddComponent<BufferFogVisual>();
            cloudGO.AddComponent<PoisonCloud>();
        }

        var cloud = cloudGO.GetComponent<PoisonCloud>();
        if (cloud == null) cloud = cloudGO.AddComponent<PoisonCloud>();

        var visual = cloudGO.GetComponent<BufferFogVisual>();
        if (visual == null) visual = cloudGO.AddComponent<BufferFogVisual>();

        // Pass our own gameObject as attacker so any "killed by" tracking
        // attributes poison damage to the Parfumer.
        cloud.Configure(
            radius: cloudRadius,
            duration: cloudDuration,
            poisonDuration: poisonDuration,
            // Nightmare's +30% reaches the poison's direct player damage too, matching melee.
            poisonDamagePerSecond: poisonDamagePerSecond * EnemyStatModifierManager.DifficultyDamageMultiplier,
            attacker: this.gameObject);

        // Reuse the Buffer's procedural fog visual, recolored green. Lightning
        // and the stasis storm are Buffer-flavored, so they're left off.
        visual.Configure(
            radius: cloudRadius,
            duration: cloudDuration,
            enableMist: cloudEnableMist,
            enableWisps: cloudEnableWisps,
            enableLightning: false,
            enableStasisStorm: false,
            mistColorOverride: cloudMistColor,
            wispColorOverride: cloudWispColor,
            outerColorOverride: cloudOuterColor);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, cloudRadius);
        Gizmos.color = new Color(0.5f, 1f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, stoppingDistance);
    }
#endif
}

