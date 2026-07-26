using System.Collections;
using UnityEngine;

public class WeaponProjectile : MonoBehaviour
{
    private float damage;
    private Vector2 direction;
    private float speed;
    private float knockBackForce;
    [SerializeField] private float bulletDespawnTime = 5f;

    [Header("Tracer")]
    [Tooltip("Colour of the short fading trail left behind the dart.")]
    [SerializeField] private Color tracerColor = new Color(0.72f, 0.38f, 1f);
    [Tooltip("Trail width at the dart, in WORLD units (independent of prefab scale).")]
    [SerializeField] private float tracerWidth = 0.14f;
    [Tooltip("How long (seconds) a point of the trail lingers.")]
    [SerializeField] private float tracerTime = 0.14f;

    private TrailRenderer tracer;
    public float GetDamage() => damage;

    // Combat telemetry: who fired this shot (co-op attribution). Null → resolved to
    // the nearest player when the hit lands (correct for single player).
    private PlayerRef _owner;
    public void SetOwner(PlayerRef owner) => _owner = owner;
    public PlayerRef GetOwner() => _owner;

    public void Initialize(Vector2 dir, float dmg, float spd, float knockback)
    {
        direction = dir.normalized;
        damage = dmg;
        speed = spd;
        knockBackForce = knockback;
    }

    private void Start()
    {
        // Ensure projectile renders above grass Y-sort range (400-1600)
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.sortingOrder = 2500;

        // Subtle warm tracer light so the shot reads in dark biomes.
        ProjectileGlow.Attach(transform, new Color(1f, 0.95f, 0.7f), worldRadius: 0.55f,
                              alpha: 0.5f, pulse: true, pulseSpeed: 10f, pulseAmount: 0.18f);

        // Point the dart's tip along its flight. `direction` never changes for a
        // straight-line bullet, so this is set once — no per-frame tracking needed.
        // Initialize() has already run by now (called on the same frame as
        // Instantiate, before Start), so `direction` is valid here.
        transform.rotation = ProjectileDart.FacingRotation(direction);
        tracer = ProjectileDart.Attach(transform, tracerColor, tracerWidth, tracerTime);

        StartCoroutine(DespawnAfterTime(bulletDespawnTime));
    }

    private IEnumerator DespawnAfterTime(float time)
    {
        yield return new WaitForSeconds(time);
        Destroy(gameObject);
    }

    private void Update()
    {
        // Space.World is REQUIRED: `direction` is a world-space vector, and the
        // transform is now rotated to face it. Translate defaults to Space.Self,
        // which would re-interpret the vector in the rotated local frame and send
        // the bullet spiralling. (It only worked before because rotation was identity.)
        transform.Translate(direction * speed * Time.deltaTime, Space.World);
    }

    // The bullet is destroyed the instant it hits something. Cut the tracer loose so
    // the tail fades out at the impact point instead of popping out of existence.
    private void OnDestroy()
    {
        ProjectileDart.Release(tracer);
        tracer = null;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Ignore triggers that should not destroy bullets
        if (other.name.Contains("EnergyCollectionTrigger") ||
            other.name.Contains("Energy") ||
            other.CompareTag("Player") ||
            other.GetComponent<PlayerMovement>() != null)
        {
            return;
        }

        if (other.CompareTag("Enemy"))
        {
            // CharacterStats (covers EnemyStats, Boss1, all bosses) 
            // GetComponent returns the most-derived type (virtual TakeDamage)
            CharacterStats stats = other.GetComponent<CharacterStats>();
            if (stats != null)
            {
                stats.TakeDamage(damage);

                // Combat telemetry: player ranged damage dealt.
                CombatStats.ReportPlayerDamageDealt(_owner, damage, other.transform.position);

                //  COMBAT FEEL — ranged hit 
                CombatJuice.OnPlayerHitEnemy(other.gameObject, isMelee: false);
                PlayHitSfx();

                // Knockback (only for enemies that have a controller)
                EnemyController enemyController = other.GetComponent<EnemyController>();
                if (enemyController != null)
                {
                    Vector2 dir = (other.transform.position - transform.position).normalized;
                    enemyController.ApplyKnockback(dir, knockBackForce);
                }

                Destroy(gameObject);
                return;
            }

            // IDamageable for Gremlin and other non-CharacterStats enemies
            IDamageable damageable = other.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, gameObject);

                // Combat telemetry: player ranged damage dealt (non-CharacterStats enemy).
                CombatStats.ReportPlayerDamageDealt(_owner, damage, other.transform.position);

                //  COMBAT FEEL  ranged hit on IDamageable 
                CombatJuice.OnPlayerHitEnemy(other.gameObject, isMelee: false);
                PlayHitSfx();

                Destroy(gameObject);
                return;
            }
        }

        // When hitting something that isn't an enemy (wall, obstacle, etc.) - destroy bullet
        // Destroy(gameObject);
        if (!other.isTrigger)
            Destroy(gameObject);
    }

    // Ranged impact SFX. Fired on any enemy hit (CharacterStats or IDamageable),
    // before the bullet is destroyed. Mortar shells use their own explosion path,
    // so this stays exclusive to the direct ranged weapon.
    private void PlayHitSfx()
    {
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.rangedHit.IsNull)
        {
            AudioManager.instance.PlaySFX(FMODEvents.instance.rangedHit, transform.position);
        }
    }
}

// Small shared helper for the two straight-line "dart" shooters — WeaponProjectile
// (player ranged) and PitcherController. Deliberately lives in this file rather than
// its own: it is a plain static class, not a MonoBehaviour, so Unity's
// filename-must-match-classname rule does not apply and nothing needs to be dragged
// onto a GameObject. Mirrors the existing ProjectileGlow.Attach pattern.
//
// Nothing else calls into it, so mortar / smoke / boomerang / grappling hook /
// turret / bomb projectiles are untouched.
public static class ProjectileDart
{
    // The bulletcrystal art points along +X at zero rotation, which is already
    // Unity's 0° = right convention, so no offset is needed. If the sprite is ever
    // redrawn pointing up, subtract 90 here and both shooters follow automatically.
    private const float SpriteForwardAngleOffset = 0f;

    /// <summary>Rotation that puts the dart's tip on <paramref name="direction"/>.</summary>
    public static Quaternion FacingRotation(Vector2 direction)
    {
        if (direction.sqrMagnitude < 1e-6f) return Quaternion.identity;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + SpriteForwardAngleOffset;
        return Quaternion.AngleAxis(angle, Vector3.forward);
    }

    /// <summary>
    /// Adds a short fading tracer behind <paramref name="target"/>. Purely cosmetic —
    /// it never moves, damages or destroys anything.
    /// </summary>
    public static TrailRenderer Attach(Transform target, Color color,
                                       float worldWidth = 0.14f, float time = 0.14f,
                                       float alpha = 0.5f)
    {
        if (target == null) return null;

        var go = new GameObject("Tracer");
        go.transform.SetParent(target, false);

        // TrailRenderer widths are multiplied by lossy scale, and the dart prefab is
        // scaled well below 1. Counter-scale the child so `worldWidth` stays honest.
        Vector3 ls = target.lossyScale;
        go.transform.localScale = new Vector3(
            Mathf.Approximately(ls.x, 0f) ? 1f : 1f / ls.x,
            Mathf.Approximately(ls.y, 0f) ? 1f : 1f / ls.y,
            1f);

        var trail = go.AddComponent<TrailRenderer>();
        trail.time = time;
        trail.startWidth = worldWidth;
        trail.endWidth = 0f;
        trail.minVertexDistance = 0.03f;
        trail.autodestruct = false;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.numCapVertices = 2;
        trail.numCornerVertices = 2;
        trail.receiveShadows = false;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.material = new Material(Shader.Find("Sprites/Default"));

        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = g;

        // Sit on the bullet's sorting layer, one step behind it.
        var sr = target.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            trail.sortingLayerID = sr.sortingLayerID;
            trail.sortingOrder = sr.sortingOrder - 1;
        }
        else
        {
            trail.sortingOrder = 2499; // above the grass Y-sort range (400–1600)
        }

        return trail;
    }

    /// <summary>
    /// Detach a tracer from a projectile that is about to be destroyed, so the tail
    /// fades where it is instead of vanishing with its parent. Safe to call with null.
    /// </summary>
    public static void Release(TrailRenderer trail)
    {
        if (trail == null) return;
        if (!trail.gameObject.scene.isLoaded) return; // scene unload / quit — leave it alone

        trail.transform.SetParent(null, true);
        trail.emitting = false;
        Object.Destroy(trail.gameObject, trail.time + 0.05f);
    }
}
