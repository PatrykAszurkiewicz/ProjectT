using UnityEngine;
using System.Collections.Generic;
using FMODUnity;

// Eye enemy. Reuses:
//   - EnemyController (movement, targeting, attack cycle, parry window)
//   - EnemyAnimationController.OnAttackFrame (frame-perfect AOE timing)
//   - EnemyStats (health, damage flash, death animation, energy drop)
//   - ParryIndicator (the "!" appears automatically — it reads parry frames
//     from EnemyData and watches IsAttacking)
//   - EnemyDamageSystem.DamageTarget (the same damage path used elsewhere)

[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(EnemyController))]
public class Eye : MonoBehaviour
{
    [Header("AOE Attack")]
    [Tooltip("Radius of the tentacle AOE around the eye, in world units.")]
    [SerializeField] private float aoeRadius = 2.0f;

    [Tooltip("If true, the AOE also damages towers and the core (anything " +
             "the eye's tentacles can physically reach). If false, only the " +
             "player is damaged. Default: true.")]
    [SerializeField] private bool aoeHitsBuildings = true;

    [Header("AOE Visual")]
    [Tooltip("Color of the AOE telegraph ring. Drawn during the parry/wind-up frames " +
             "so the player can see the danger area before the hit lands.")]
    [SerializeField] private Color telegraphColor = new Color(0.85f, 0.2f, 1f, 0.55f);

    [Tooltip("Color of the AOE strike flash on the hit frame.")]
    [SerializeField] private Color strikeColor = new Color(1f, 0.3f, 1f, 0.9f);

    [Tooltip("How long the strike flash lingers after the hit frame, in seconds.")]
    [SerializeField] private float strikeFlashDuration = 0.25f;

    [Header("Attack Dust")]
    [Tooltip("If true, the eye kicks up a small dust shockwave on the hit frame — " +
             "same style as the boss death dust, scaled down for a small enemy. " +
             "Reuses HammerSlamSystem.GetSoftDiscSprite() so the look matches.")]
    [SerializeField] private bool emitAttackDust = true;

    [Tooltip("How far the dust puffs travel from the eye, in world units. " +
             "Should roughly match aoeRadius so the dust visually maps to the danger zone.")]
    [SerializeField] private float dustMaxRadius = 2.5f;

    [Tooltip("Number of dust puffs in the ring. Boss uses 14; small enemies look " +
             "right around 6–8.")]
    [SerializeField] private int dustPuffCount = 7;

    [Tooltip("Color of the dust. Default is a dusky purple to match the eye's chains.")]
    [SerializeField] private Color dustColor = new Color(0.55f, 0.35f, 0.65f, 1f);

    [Tooltip("If true, spawns a second layer of earth-colored dust underneath the " +
             "primary dust. Reads as ground being kicked up by the tentacle strike — " +
             "complements the magical purple primary dust with something grounded.")]
    [SerializeField] private bool emitEarthDust = true;

    [Tooltip("Color of the earth dust layer. Default is a warm dusty brown.")]
    [SerializeField] private Color earthDustColor = new Color(0.50f, 0.35f, 0.22f, 1f);

    [Tooltip("Number of earth-dust puffs in the lower ring. Usually a touch fewer " +
             "than the primary puffs so it looks like a separate layer, not a duplicate.")]
    [SerializeField] private int earthDustPuffCount = 5;

    [Tooltip("Radius the earth dust travels to, in world units. Usually a bit shorter " +
             "than dustMaxRadius so the earth layer stays tighter to the eye and " +
             "doesn't fight the primary dust's silhouette.")]
    [SerializeField] private float earthDustMaxRadius = 1.8f;

    [Tooltip("Vertical offset for the EARTH dust origin — negative pulls the brown " +
             "layer below the pivot so it reads as ground dust rising up. " +
             "Independent of dustYOffset (which moves the primary purple dust).")]
    [SerializeField] private float earthDustYOffset = -0.5f;

    [Tooltip("Vertical offset for the PRIMARY (purple) dust origin in world units. " +
             "0 = dust spawns at the eye's pivot. Positive shifts the dust upward, " +
             "negative downward. Tune if the dust looks misaligned with the sprite.")]
    [SerializeField] private float dustYOffset = 0.0f;

    [Tooltip("Logs to Console every time the dust is spawned. Use to confirm the " +
             "AOE is actually firing if you can't see the dust on screen — if you " +
             "see the log but no dust, it's a sorting/scale problem; no log = " +
             "OnAttackFrame isn't reaching the hitFrame.")]
    [SerializeField] private bool debugLogs = false;

    // Cached refs
    private EnemyStats stats;
    private EnemyController controller;
    private EnemyAnimationController animController;

    // Frame config copied from EnemyData on Start. Kept here so we don't read
    // through EnemyData every OnAttackFrame call (which fires once per frame).
    private int hitFrame;
    private int parryFrameStart;
    private int parryFrameEnd;

    // Visual ring (created on demand the first time the eye telegraphs).
    private GameObject ringGO;
    private LineRenderer ringLR;
    private float ringFlashRemaining = 0f;
    private bool ringTelegraphing = false;

    // Fallback path: when attack.frameCount == 0 the animation controller's
    // frame loop never runs and OnAttackFrame never fires. In that case we
    // watch EnemyController.IsAttacking and fire the AOE on the rising edge.
    // Set in Start(); checked in Update().
    private bool useTimerFallback = false;
    private bool wasAttackingLastFrame = false;

    // Continuous attack loop (EyeAttack). Started while the Eye is attacking and
    // stopped when it stops attacking or dies. The FMOD event should be authored
    // as a looping event; this just controls when it plays.
    private FMOD.Studio.EventInstance attackLoop;
    private bool attackLoopActive = false;

    private void Start()
    {
        stats = GetComponent<EnemyStats>();
        controller = GetComponent<EnemyController>();
        animController = GetComponent<EnemyAnimationController>();

        int atkCount = (stats != null && stats.enemyData != null) ? stats.enemyData.attack.frameCount : -1;

        // Validation
        bool framesConfigured = atkCount > 0;
        if (!framesConfigured)
        {
            Debug.LogWarning($"[Eye] EnemyData.attack.frameCount is {atkCount} on {gameObject.name}. " +
                             "Frame events will not fire, so the Eye will use a TIMER-based fallback " +
                             "to spawn the AOE/dust on each attack cycle. " +
                             "Once you add attack sprites, set attack.frameCount to the real number " +
                             "and the frame-event path will take over automatically.");
            useTimerFallback = true;
        }

        // Read frame config from the EnemyData asset (same source ParryIndicator
        // and EnemyController use). One source of truth.
        if (stats != null && stats.enemyData != null)
        {
            hitFrame = Mathf.Max(stats.enemyData.hitFrame, 0);
            parryFrameStart = Mathf.Max(stats.enemyData.parryFrameStart, 0);
            parryFrameEnd = Mathf.Max(stats.enemyData.parryFrameEnd, 0);
            if (parryFrameEnd < parryFrameStart) parryFrameEnd = parryFrameStart;

            // Degenerate case: with a single attack frame, every frame index is
            // both the "telegraph" frame and the "hit" frame. Showing the ring
            // is pointless (no wind-up to react to) and looks like a permanent
            // glow. Disable the telegraph until there's a real wind-up.
            // -1 = "no valid parry window" sentinel.
            if (stats.enemyData.attack.frameCount <= 1)
            {
                parryFrameStart = -1;
                parryFrameEnd = -1;
            }
        }

        // Subscribe to per-frame events from the animation controller. The
        // controller fires this once per frame during the attack animation,
        // and stops firing if the animation is interrupted (e.g. parry stun
        // or death) — which gives us free "abort on parry" behaviour.
        if (animController != null)
        {
            animController.OnAttackFrame += HandleAttackFrame;
        }
        else
        {
            Debug.LogError($"[Eye] No EnemyAnimationController on {gameObject.name} — frame events will not fire and no dust/AOE will trigger.");
        }

        BuildRing();
    }

    private void OnDestroy()
    {
        if (animController != null)
            animController.OnAttackFrame -= HandleAttackFrame;

        StopAttackLoop();
    }

    private void OnDisable()
    {
        // Dying/pooling disables the object — never leave the loop droning on.
        StopAttackLoop();
    }

    // Starts the loop on the rising edge of IsAttacking and stops it on the
    // falling edge. Also keeps the 3D position on the Eye while it plays.
    private void UpdateAttackLoop()
    {
        bool attackingNow = controller != null && controller.IsAttacking;

        if (attackingNow && !attackLoopActive) StartAttackLoop();
        else if (!attackingNow && attackLoopActive) StopAttackLoop();

        if (attackLoopActive && attackLoop.isValid())
            attackLoop.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(transform.position));
    }

    private void StartAttackLoop()
    {
        if (AudioManager.instance == null || FMODEvents.instance == null) return;
        if (FMODEvents.instance.eyeAttack.IsNull) return;

        attackLoop = FMODUnity.RuntimeManager.CreateInstance(FMODEvents.instance.eyeAttack);
        attackLoop.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(transform.position));
        attackLoop.start();
        attackLoopActive = true;
    }

    private void StopAttackLoop()
    {
        if (!attackLoopActive) return;
        attackLoopActive = false;

        if (attackLoop.isValid())
        {
            attackLoop.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            attackLoop.release();
        }
    }

    private void Update()
    {
        // Continuous attack sound: play while the Eye is attacking, stop the moment
        // it stops (or dies — OnDisable/OnDestroy also stop it). Runs on both the
        // frame-event and timer-fallback paths since it keys off IsAttacking.
        UpdateAttackLoop();

        // Linger flash after the hit, then return to invisible.
        if (ringFlashRemaining > 0f)
        {
            ringFlashRemaining -= Time.deltaTime;
            float t = Mathf.Clamp01(ringFlashRemaining / strikeFlashDuration);
            SetRingColor(Color.Lerp(telegraphColor, strikeColor, t), t);
            if (ringFlashRemaining <= 0f)
                SetRingVisible(false);
        }

        // Fallback path
        if (useTimerFallback && controller != null)
        {
            bool nowAttacking = controller.IsAttacking;
            if (nowAttacking && !wasAttackingLastFrame)
            {
                if (debugLogs) Debug.Log($"[Eye] Timer-fallback AOE fired on {gameObject.name}");
                FireAOE();
                ringFlashRemaining = strikeFlashDuration;
                SetRingVisible(true);
                SetRingColor(strikeColor, 1f);
            }
            wasAttackingLastFrame = nowAttacking;
        }
    }

    // Called every frame of the attack animation with the 0-based frame index
    // relative to the attack animation's start frame.
    private void HandleAttackFrame(int frameIndex)
    {
        if (debugLogs) Debug.Log($"[Eye] HandleAttackFrame({frameIndex}) — hitFrame={hitFrame}");
        // Telegraph the AOE during the parry window
        if (frameIndex >= parryFrameStart && frameIndex <= parryFrameEnd)
        {
            if (!ringTelegraphing)
            {
                ringTelegraphing = true;
                SetRingVisible(true);
                SetRingColor(telegraphColor, 1f);
            }
        }

        // Fire the AOE on the configured hit frame.
        if (frameIndex == hitFrame)
        {
            FireAOE();
            ringTelegraphing = false;
            ringFlashRemaining = strikeFlashDuration;
            SetRingColor(strikeColor, 1f);
        }
        else if (frameIndex > hitFrame && ringTelegraphing)
        {
            // Past the hit but still inside the attack animation — kill the
            // telegraph so it doesn't keep glowing on follow-through frames.
            ringTelegraphing = false;
            if (ringFlashRemaining <= 0f) SetRingVisible(false);
        }
    }

    private void FireAOE()
    {
        // Spawn the dust ring 
        if (emitAttackDust)
            SpawnAttackDust();

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, aoeRadius);
        if (hits == null || hits.Length == 0) return;

        // Deduplicate: a tower might have multiple colliders. Track by GO.
        var hitGOs = new HashSet<GameObject>();

        float damage = stats != null ? stats.Damage : 0f;
        if (damage <= 0f) return;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            // Skip self.
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

            // Skip other enemies (the eye shouldn't kill its allies with AOE).
            var otherEnemy = col.GetComponentInParent<EnemyStats>();
            if (otherEnemy != null) continue;

            GameObject targetGO = ResolveDamageTarget(col);
            if (targetGO == null) continue;
            if (!hitGOs.Add(targetGO)) continue; // already hit this GO this pulse

            // Player: route through EnemyDamageSystem so shield-block helper
            // gets a chance to intercept (same path used by everything else).
            if (targetGO.CompareTag("Player"))
            {

                if (useTimerFallback) continue;

                if (EnemyDamageSystem.Instance != null)
                    EnemyDamageSystem.Instance.DamageTarget(targetGO, damage, gameObject);
                else
                {
                    var cs = targetGO.GetComponent<CharacterStats>();
                    if (cs != null) cs.TakeDamage(damage);
                }
                continue;
            }

            // Buildings (towers / core). Optional.
            if (!aoeHitsBuildings) continue;

            // Energy consumers (Core, towers that implement IEnergyConsumer)
            var consumer = targetGO.GetComponent<IEnergyConsumer>();
            if (consumer != null)
            {
                if (EnergyManager.Instance != null)
                    EnergyManager.Instance.DamageEnergyConsumer(consumer, damage, gameObject);
                continue;
            }

            // Anything else with a CharacterStats (e.g. a destructible prop)
            var stats2 = targetGO.GetComponent<CharacterStats>();
            if (stats2 != null)
                stats2.TakeDamage(damage);
        }
    }

    // Picks the right GameObject to damage for a given collider.
    private static GameObject ResolveDamageTarget(Collider2D col)
    {
        if (col == null) return null;

        // Walk up to find a tagged root or a CharacterStats / IEnergyConsumer holder.
        Transform t = col.transform;
        while (t != null)
        {
            if (t.CompareTag("Player") || t.CompareTag("Core") || t.CompareTag("Tower"))
                return t.gameObject;
            if (t.GetComponent<CharacterStats>() != null) return t.gameObject;
            if (t.GetComponent<IEnergyConsumer>() != null) return t.gameObject;
            t = t.parent;
        }
        return col.gameObject;
    }

    // VISUAL RING
    private void BuildRing()
    {
        ringGO = new GameObject("EyeAOERing");
        ringGO.transform.SetParent(transform, false);
        ringGO.transform.localPosition = Vector3.zero;

        ringLR = ringGO.AddComponent<LineRenderer>();
        ringLR.useWorldSpace = false;
        ringLR.loop = true;
        ringLR.positionCount = 48;
        ringLR.startWidth = 0.08f;
        ringLR.endWidth = 0.08f;
        ringLR.numCornerVertices = 2;
        ringLR.sortingLayerName = "Default";
        ringLR.sortingOrder = 110; // above ground, below sprite

        // Use a basic unlit material so vertex colors show through.
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        var mat = new Material(sh);
        mat.mainTexture = Texture2D.whiteTexture;
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        ringLR.material = mat;

        // Bake the circle once 
        float s = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, 0.001f);
        float localRadius = aoeRadius / s;
        for (int i = 0; i < ringLR.positionCount; i++)
        {
            float a = (i / (float)ringLR.positionCount) * Mathf.PI * 2f;
            ringLR.SetPosition(i, new Vector3(Mathf.Cos(a) * localRadius, Mathf.Sin(a) * localRadius, 0f));
        }

        SetRingVisible(false);
    }

    private void SetRingVisible(bool visible)
    {
        if (ringLR != null) ringLR.enabled = visible;
    }

    private void SetRingColor(Color c, float intensity)
    {
        if (ringLR == null) return;
        Color a = c;
        Color b = c; b.a *= 0.3f;
        a.a *= intensity;
        b.a *= intensity;
        ringLR.startColor = a;
        ringLR.endColor = b;
    }

    // ATTACK DUST
    private void SpawnAttackDust()
    {
        Sprite puffSprite = GetSoftDiscSprite();
        if (puffSprite == null)
        {
            if (debugLogs) Debug.LogWarning("[Eye] Dust skipped — soft disc sprite was null.");
            return;
        }

        if (debugLogs) Debug.Log($"[Eye] Spawning attack dust at {transform.position} (puffs={dustPuffCount}, radius={dustMaxRadius})");

        // Pick a sorting layer/order that matches whatever this enemy uses,
        // so the dust composites with the world the same way other VFX do.
        var sr = GetComponentInChildren<SpriteRenderer>();
        string sortLayerName = sr != null ? sr.sortingLayerName : "Default";
        int sortOrder = sr != null ? sr.sortingOrder : 10;

        // World-space root so the dust doesn't follow the eye after launch.
        GameObject root = new GameObject("EyeAttackDust");
        root.transform.position = transform.position; // root is at pivot; child offsets handle layer Y shifts

        // Host the dust animation ON the root itself, not on the Eye. Coroutines
        // started on the Eye are killed the instant the Eye's GameObject is
        // destroyed — so if the Eye died mid-attack, the puff coroutines froze
        // and DestroyAfter never ran, orphaning this root as a permanent smear
        // of dust on the ground. Running everything on the root makes the effect
        // finish and clean itself up regardless of the Eye's lifetime.
        var host = root.AddComponent<EyeAttackDustRunner>();

        // PRIMARY DUST LAYER 
        SpawnDustLayer(host, root.transform, puffSprite, sortLayerName, sortOrder,
                       dustPuffCount, dustMaxRadius, dustColor, dustYOffset,
                       baseSortOffset: 24);

        // EARTH DUST LAYER
        if (emitEarthDust)
        {
            SpawnDustLayer(host, root.transform, puffSprite, sortLayerName, sortOrder,
                           earthDustPuffCount, earthDustMaxRadius, earthDustColor, earthDustYOffset,
                           baseSortOffset: 22);
        }

        // Tear the root down after the longest-lived puff has finished. This is
        // an engine-scheduled destroy (not a coroutine on the Eye), so it fires
        // even if the Eye is destroyed the same frame the dust spawns.
        Object.Destroy(root, 1.2f);
    }

    // Spawns one dust layer 
    private void SpawnDustLayer(
        MonoBehaviour host,
        Transform parent, Sprite puffSprite, string sortLayerName, int sortOrderBase,
        int puffCount, float maxRadius, Color color, float yOffset, int baseSortOffset)
    {
        // Layer host so each layer's puffs/disc share their own Y offset
        // without polluting the root transform.
        var layer = new GameObject("DustLayer");
        layer.transform.SetParent(parent, false);
        layer.transform.localPosition = new Vector3(0f, yOffset, 0f);

        // Soft ground-hugging disc. Driven by `host` (the dust root) so it keeps
        // animating after the Eye is gone.
        host.StartCoroutine(EyeDustDisc(layer.transform, puffSprite, sortLayerName,
                                   sortOrderBase + baseSortOffset, maxRadius, color));

        int puffs = Mathf.Max(1, puffCount);
        for (int i = 0; i < puffs; i++)
        {
            float ang = (i / (float)puffs) * Mathf.PI * 2f + Random.Range(-0.12f, 0.12f);
            host.StartCoroutine(EyeDustPuff(layer.transform, puffSprite, sortLayerName,
                                       sortOrderBase + baseSortOffset + 1, i, ang,
                                       maxRadius, color));
        }
    }

    private System.Collections.IEnumerator EyeDustPuff(
        Transform parent, Sprite puffSprite, string sortLayerName, int sortOrder, int index, float angle,
        float maxRadius, Color color)
    {
        if (parent == null) yield break;

        var go = new GameObject("DustPuff");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = puffSprite;
        sr.sortingLayerName = sortLayerName;
        sr.sortingOrder = sortOrder + (index % 5);

        Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        float startR = maxRadius * Random.Range(0.40f, 0.55f);
        // Travel a bit beyond maxRadius so the dust extends past the AOE ring.
        float endR = maxRadius * Random.Range(1.20f, 1.50f);
        // Longer life = slower drift = reads as "settling" dust rather than a poof.
        float life = Random.Range(0.75f, 1.05f);
        float startSize = maxRadius * Random.Range(0.28f, 0.40f);
        float endSize = maxRadius * Random.Range(0.65f, 0.95f);
        float spin = Random.Range(-80f, 80f);

        float e = 0f;
        while (e < life && go != null)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / life);
            // Ease-out travel: fast launch, decelerating like real dust.
            float travel = 1f - (1f - t) * (1f - t);
            float r = Mathf.Lerp(startR, endR, travel);
            go.transform.localPosition = (Vector3)(dir * r);
            go.transform.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, t);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, spin * t);

            Color c = color;
            c.a = Mathf.Lerp(0.85f, 0f, t * t);
            sr.color = c;
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private System.Collections.IEnumerator EyeDustDisc(
        Transform parent, Sprite puffSprite, string sortLayerName, int sortOrder,
        float maxRadius, Color color)
    {
        if (parent == null) yield break;

        var go = new GameObject("DustDisc");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = puffSprite;
        sr.sortingLayerName = sortLayerName;
        sr.sortingOrder = sortOrder;

        float life = 0.75f;
        float e = 0f;
        while (e < life && go != null)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            float eased = 1f - (1f - p) * (1f - p);
            // Start wider and end bigger so the disc visibly hugs the AOE area
            // rather than sitting tight under the eye.
            go.transform.localScale =
                Vector3.one * Mathf.Lerp(maxRadius * 0.60f, maxRadius * 2.4f, eased);
            Color c = color;
            c.a = Mathf.Lerp(0.55f, 0f, p);
            sr.color = c;
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    // Procedural soft circle for dust puffs
    private static Sprite _softDiscSprite;
    private static Sprite GetSoftDiscSprite()
    {
        if (_softDiscSprite != null) return _softDiscSprite;

        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color[S * S];
        Vector2 center = new Vector2(S * 0.5f, S * 0.5f);
        float maxD = S * 0.5f;

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center) / maxD;
                // Soft falloff: a^3 gives a quick fade near the edge without a hard cutoff.
                float a = Mathf.Clamp01(1f - d);
                px[y * S + x] = new Color(1f, 1f, 1f, a * a * a);
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        _softDiscSprite = Sprite.Create(tex, new Rect(0, 0, S, S),
                                        new Vector2(0.5f, 0.5f), S);
        return _softDiscSprite;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.85f, 0.2f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, aoeRadius);
    }
}

// Lightweight coroutine host that lives on the world-space attack-dust root.
// The dust animation and cleanup run here — independent of the Eye — so killing
// the Eye mid-attack can no longer freeze puffs or leave dust stuck on the
// ground. Added at runtime via AddComponent; it needs no state of its own.
public sealed class EyeAttackDustRunner : MonoBehaviour { }

