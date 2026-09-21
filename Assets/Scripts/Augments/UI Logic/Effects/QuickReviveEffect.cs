using UnityEngine;
using System.Collections;

public class QuickReviveEffect : MonoBehaviour
{
    [Header("Revive Settings")]
    public float reviveHealthPercentage = 0.5f; // 50% health after revive
    public float reviveInvulnerabilityDuration = 2f; // Brief invulnerability after revive
    public float reviveDelay = 0.1f; // Small delay before revival

    [Header("Visual Feedback")]
    public Color reviveFlashColor = new Color(0f, 1f, 0.5f, 1f); // Green flash
    public float reviveFlashDuration = 0.3f;
    public int reviveFlashCount = 3;

    [Header("Self-revive screen effect")]
    [Tooltip("Show the downed grey screen + pulsing damage vignette across the self-revive " +
             "moment. The window itself is only reviveDelay long (~0.1s), far too short to " +
             "read, so the effect is held for screenEffectSeconds instead. Split-screen safe: " +
             "it lands only on THIS player's camera.")]
    public bool screenEffectOnRevive = true;

    [Tooltip("How long the grey screen + vignette is held for a self-revive.")]
    public float screenEffectSeconds = 0.7f;

    private bool hasBeenUsedThisWave = false;
    private PlayerStats playerStats;
    private SpriteRenderer spriteRenderer;
    private bool isProcessingRevive = false;
    private int lastWaveNumber = -1;

    // Cached so the downed guard below costs nothing per frame.
    private PlayerDownedState downedState;
    private bool downedStateResolved;

    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (playerStats == null)
        {
            Debug.LogError("[QUICK_REVIVE] PlayerStats component not found!");
            enabled = false;
            return;
        }
    }

    void Start()
    {
        // Start monitoring wave changes for reset
        StartCoroutine(MonitorWaveChanges());
        //Debug.Log("[QUICK_REVIVE] Quick Revive effect initialized ");
    }

    private IEnumerator MonitorWaveChanges()
    {
        yield return new WaitForSeconds(0.5f); // Initial delay

        WaveSpawner spawner = FindFirstObjectByType<WaveSpawner>();

        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            if (spawner != null)
            {
                int currentWave = spawner.GetCurrentWaveIndex();
                if (currentWave != lastWaveNumber && lastWaveNumber != -1)
                {
                    ResetRevive();
                }
                lastWaveNumber = currentWave;
            }
        }
    }

    // True while this player is in the co-op / respawn DOWNED state: alive object,
    // pinned at 0 HP, control cut, awaiting a teammate revive or a respawn countdown.
    //
    // WHY THIS GUARD EXISTS. Quick Revive triggers off a polled IsDead() check rather
    // than off Die(), and a downed player is pinned at 0 HP — so IsDead() is true for
    // the entire downed window. Without this guard, any path that recharges the augment
    // while the player is down (the wave rolling over is the obvious one) would fire
    // PerformRevive and call SetHealthAndNotify on a player whose PlayerDownedState is
    // still IsDowned. The result is a "zombie": 50% HP, but control frozen, colliders
    // off and the prone animation still playing — and, worse, PlayerRegistry.AllDead()
    // now returns false, so EvaluateTeamWipe can never fire and a co-op team wipe
    // silently fails to end the run.
    //
    // Standing the player back up from here instead would be the other option, but a
    // respawn may already be pending on them (PlayerRespawnController marks itself
    // pending before calling EnterDowned), and un-downing behind its back risks a
    // double revive. Leaving the downed player to the teammate revive / respawn
    // countdown is the safe half of that choice: the augment simply stays charged,
    // because it was never spent.
    private bool IsDownedNow()
    {
        if (!downedStateResolved)
        {
            downedState = GetComponent<PlayerDownedState>();
            // The component is added on demand by PlayerStats.Die(), so keep looking
            // until one actually exists.
            if (downedState != null) downedStateResolved = true;
        }
        return downedState != null && downedState.IsDowned;
    }

    void Update()
    {
        // Check if player is dead and revive is available
        // This needs to happen immediately when IsDead() becomes true
        if (playerStats.IsDead() && !hasBeenUsedThisWave && !isProcessingRevive)
        {
            if (IsDownedNow()) return;   // see IsDownedNow — not ours to revive

            //Debug.Log("[QUICK_REVIVE] Death detected Triggering revive");
            StartCoroutine(PerformRevive());
        }
    }

    private IEnumerator PerformRevive()
    {
        isProcessingRevive = true;
        hasBeenUsedThisWave = true;

        //Debug.Log("[QUICK_REVIVE] Player died Activating Quick Revive...");

        // Play revive sound effect
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            //TODO add revive sound
            //AudioManager.instance.PlayOneShot(FMODEvents.instance.weaponPickup, transform.position);
        }

        // Grey screen + pulsing damage vignette on THIS player's half, held long enough
        // to actually read. Started before the delay so the effect covers the whole
        // self-revive beat rather than appearing after it. Same per-camera component
        // build mode uses, so split-screen isolation comes for free.
        if (screenEffectOnRevive)
        {
            var pref = GetComponent<PlayerRef>();
            var cam = PlayerRef.ResolveCameraFor(pref);
            if (cam != null)
            {
                bool showVignette = PlayerDamageVignette.Mode != PlayerDamageVignette.VignetteMode.Off;
                PlacementModeScreenEffect
                    .Ensure(cam, pref != null ? pref.PlayerIndex : 0)
                    .PulseDowned(screenEffectSeconds, showVignette);
            }
        }

        // Small delay for dramatic effect
        yield return new WaitForSeconds(reviveDelay);

        // RACE GUARD. IsAvailable() returns false the moment isProcessingRevive is set,
        // so a second damage tick landing inside this delay (poison, an aura, contact
        // damage — anything faster than reviveDelay) falls straight past the Quick Revive
        // branch in PlayerStats.Die() and into the downed / respawn path. Healing now
        // would produce exactly the zombie state described on IsDownedNow. Bail instead,
        // and refund the charge since the revive never happened.
        if (IsDownedNow())
        {
            hasBeenUsedThisWave = false;
            isProcessingRevive = false;
            yield break;
        }

        // Revive player with 50% health
        float reviveHealth = playerStats.maxHealth * reviveHealthPercentage;
        playerStats.SetHealthAndNotify(reviveHealth);

        //Debug.Log($"[QUICK_REVIVE] Player revived with {reviveHealth:F0}/{playerStats.maxHealth:F0} HP ({reviveHealthPercentage * 100}%)");

        // Visual feedback - revive flash
        StartCoroutine(ReviveFlashEffect());

        // Grant brief invulnerability after revive
        GrantTemporaryInvulnerability();

        isProcessingRevive = false;
    }

    private void GrantTemporaryInvulnerability()
    {
        // Check if ImmunityPhasesEffect already exists
        var permanentImmunity = GetComponent<ImmunityPhasesEffect>();

        if (permanentImmunity != null)
        {
            //Debug.Log("[QUICK_REVIVE] Player has permanent immunity augment - skipping temporary immunity");
            return;
        }

        // Add temporary immunity component
        var tempImmunity = gameObject.GetComponent<TemporaryReviveImmunity>();
        if (tempImmunity != null)
        {
            Destroy(tempImmunity);
        }

        tempImmunity = gameObject.AddComponent<TemporaryReviveImmunity>();
        tempImmunity.Initialize(reviveInvulnerabilityDuration);

        //Debug.Log($"[QUICK_REVIVE] Granted {reviveInvulnerabilityDuration}s temporary invulnerability");
    }

    private IEnumerator ReviveFlashEffect()
    {
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;

        for (int i = 0; i < reviveFlashCount; i++)
        {
            spriteRenderer.color = reviveFlashColor;
            yield return new WaitForSeconds(reviveFlashDuration / (reviveFlashCount * 2));

            spriteRenderer.color = originalColor;
            yield return new WaitForSeconds(reviveFlashDuration / (reviveFlashCount * 2));
        }

        spriteRenderer.color = originalColor;
    }

    private void ResetRevive()
    {
        if (hasBeenUsedThisWave)
        {
            hasBeenUsedThisWave = false;
            //Debug.Log("[QUICK_REVIVE] 🔄 Revive recharged for new wave!");
        }
    }

    void OnDestroy()
    {
        StopAllCoroutines();
    }

    // Public getters for UI
    public bool IsAvailable() => !hasBeenUsedThisWave && !isProcessingRevive;
    public bool HasBeenUsed() => hasBeenUsedThisWave;
}

// Temporary immunity component specifically
public class TemporaryReviveImmunity : MonoBehaviour
{
    private float endTime;
    private bool isActive = false;

    public void Initialize(float seconds)
    {
        endTime = Time.time + seconds;
        isActive = true;
        //Debug.Log($"[TEMP_IMMUNITY] Activated for {seconds} seconds");
    }

    void Update()
    {
        if (isActive && Time.time >= endTime)
        {
            //Debug.Log("[TEMP_IMMUNITY] Temporary immunity expired");
            Destroy(this);
        }
    }

    public bool ShouldBlockDamage()
    {
        return isActive && Time.time < endTime;
    }
}


