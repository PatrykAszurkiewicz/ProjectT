using UnityEngine;
using System.Collections;

public class BerserkerModeEffect : MonoBehaviour
{
    [Header("CSV-Driven Parameters")]
    [System.NonSerialized] public float damagePerMissingHealthPercent = 0.02f; // 2% damage per 1% missing health
    [System.NonSerialized] public float defensePenalty = 0.25f; // 25% more damage taken

    [Header("Visual Feedback")]
    [SerializeField] private Color lowHealthColor = new Color(1f, 0f, 0f, 0.8f); // Red tint
    [SerializeField] private float maxColorIntensity = 0.6f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private PlayerStats playerStats;
    private Weapon weapon;
    private WeaponData weaponData;
    private SpriteRenderer spriteRenderer;

    private float baseWeaponDamage;
    private Color originalColor;

    // Ownership tracking for weaponData.damage.
    //
    // This effect WRITES weaponData.damage every frame, which made it hostile to
    // everything else that touches weapon damage:
    //   * a weapon hot-swap (augments 2 / 66 / 93 / 318 / 327 build a FRESH runtime
    //     WeaponData copy) left it writing to the orphaned old copy forever, so the
    //     berserker bonus silently stopped applying to the new weapon;
    //   * any damage augment picked AFTER this effect initialised was overwritten on
    //     the very next frame, because baseWeaponDamage was a one-time snapshot.
    //
    // We now remember the exact value we last wrote. If we read back something
    // different, someone else changed the field, and we recover the NEW base by
    // dividing out the multiplier we had applied — so external changes stick and
    // compose with the berserker bonus instead of fighting it.
    private float lastWrittenDamage = float.NaN;
    private float lastAppliedMultiplier = 1f;

    private bool isInitialized = false;
    private float lastHealthPercent = 1f;

    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (playerStats == null)
        {
            Debug.LogError("[BERSERKER_MODE] PlayerStats not found!");
            enabled = false;
            return;
        }

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
    }

    void Start()
    {
        // Validate CSV values
        if (Mathf.Approximately(damagePerMissingHealthPercent, 0f))
        {
            Debug.LogWarning("[BERSERKER_MODE] damagePerMissingHealthPercent is 0! Using default 0.02");
            damagePerMissingHealthPercent = 0.02f;
        }

        StartCoroutine(InitializeWeaponReference());

        //Debug.Log($"[BERSERKER_MODE] Effect initialized from CSV:");
        //Debug.Log($"  Damage per 1% missing health: {damagePerMissingHealthPercent * 100}%");
        //Debug.Log($"  Defense penalty: {defensePenalty * 100}% (player takes {(1f + defensePenalty) * 100}% damage)");
    }

    private IEnumerator InitializeWeaponReference()
    {
        yield return null;

        // Find weapon
        weapon = GetComponentInChildren<Weapon>();
        if (weapon == null)
        {
            weapon = FindFirstObjectByType<Weapon>();
        }

        if (weapon == null)
        {
            Debug.LogError("[BERSERKER_MODE] Weapon not found!");
            enabled = false;
            yield break;
        }

        yield return null;

        weaponData = weapon.GetWeaponData();
        if (weaponData == null)
        {
            Debug.LogError("[BERSERKER_MODE] WeaponData not found!");
            enabled = false;
            yield break;
        }

        baseWeaponDamage = weaponData.damage;
        lastWrittenDamage = float.NaN;
        lastAppliedMultiplier = 1f;
        isInitialized = true;

        //Debug.Log($"[BERSERKER_MODE] Weapon initialized - Base damage: {baseWeaponDamage}");
    }

    void Update()
    {
        if (!isInitialized || playerStats.IsDead())
            return;

        UpdateDamageBasedOnHealth();
        UpdateVisualEffect();
    }

    private void UpdateDamageBasedOnHealth()
    {
        // Re-resolve the weapon's CURRENT runtime data every frame. A hot-swap
        // replaces the WeaponData object outright; without this we would keep writing
        // to the discarded copy and the bonus would vanish for the new weapon.
        WeaponData live = weapon != null ? weapon.GetWeaponData() : null;
        if (live == null) return;

        if (!ReferenceEquals(live, weaponData))
        {
            // Weapon swapped. Put the OLD weapon's damage back, but only if it still
            // holds the value we wrote — if something else changed it since, that
            // change is theirs to keep.
            if (weaponData != null && !float.IsNaN(lastWrittenDamage)
                && Mathf.Approximately(weaponData.damage, lastWrittenDamage))
            {
                weaponData.damage = baseWeaponDamage;
            }

            weaponData = live;
            baseWeaponDamage = live.damage;
            lastWrittenDamage = float.NaN;
            lastAppliedMultiplier = 1f;
        }
        else if (!float.IsNaN(lastWrittenDamage)
                 && !Mathf.Approximately(weaponData.damage, lastWrittenDamage))
        {
            // Same weapon, but the field moved without us: another augment applied a
            // damage change on top of our written value. Divide our own multiplier
            // back out to recover the new true base, so their change persists instead
            // of being erased on this frame.
            baseWeaponDamage = lastAppliedMultiplier > 0.0001f
                ? weaponData.damage / lastAppliedMultiplier
                : weaponData.damage;
        }

        float currentHealthPercent = playerStats.currentHealth / playerStats.maxHealth;
        float missingHealthPercent = 1f - currentHealthPercent;

        // Calculate damage multiplier
        // Formula: baseDamage * (1 + (missingHealthPercent * 100 * damagePerMissingHealthPercent))
        // Example: At 50% missing health with 0.02 rate:
        //   1 + (0.50 * 100 * 0.02) = 1 + 1.0 = 2.0x damage

        float damageMultiplier = 1f + (missingHealthPercent * 100f * damagePerMissingHealthPercent);
        float newDamage = baseWeaponDamage * damageMultiplier;

        weaponData.damage = newDamage;
        lastWrittenDamage = newDamage;
        lastAppliedMultiplier = damageMultiplier;

        // Log when health changes significantly
        if (showDebugLogs && Mathf.Abs(currentHealthPercent - lastHealthPercent) > 0.05f)
        {
            lastHealthPercent = currentHealthPercent;
            //Debug.Log($"[BERSERKER_MODE] Health: {currentHealthPercent * 100:F1}%, Missing: {missingHealthPercent * 100:F1}%, Damage: {baseWeaponDamage} → {newDamage:F1} ({damageMultiplier:F2}x)");
        }
    }

    private void UpdateVisualEffect()
    {
        if (spriteRenderer == null) return;

        float currentHealthPercent = playerStats.currentHealth / playerStats.maxHealth;
        float missingHealthPercent = 1f - currentHealthPercent;

        // Intensify red tint as health decreases
        float colorIntensity = missingHealthPercent * maxColorIntensity;
        Color targetColor = Color.Lerp(originalColor, lowHealthColor, colorIntensity);
        spriteRenderer.color = targetColor;
    }

    // This method is called by PlayerStats BEFORE damage is applied
    public float ModifyIncomingDamage(float incomingDamage)
    {
        // Increase damage taken by defense penalty
        float multiplier = 1f + defensePenalty;
        float modifiedDamage = incomingDamage * multiplier;

        if (showDebugLogs)
        {
            Debug.Log($"[BERSERKER_MODE] Incoming damage: {incomingDamage} → {modifiedDamage} ({multiplier}x due to {defensePenalty * 100}% penalty)");
        }

        return modifiedDamage;
    }

    void OnDestroy()
    {
        // Restore original values — but only if the field still holds what we wrote.
        // If another system changed it after us, that value is theirs, and stamping
        // our stale base over it would silently revert their augment.
        if (isInitialized && weaponData != null
            && (float.IsNaN(lastWrittenDamage) || Mathf.Approximately(weaponData.damage, lastWrittenDamage)))
        {
            weaponData.damage = baseWeaponDamage;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        //Debug.Log("[BERSERKER_MODE] Effect removed, stats restored");
    }

    void OnDisable()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }

    // Public getters for UI
    public float GetCurrentDamageMultiplier()
    {
        if (!isInitialized || playerStats == null) return 1f;

        float currentHealthPercent = playerStats.currentHealth / playerStats.maxHealth;
        float missingHealthPercent = 1f - currentHealthPercent;
        return 1f + (missingHealthPercent * 100f * damagePerMissingHealthPercent);
    }

    public float GetDefensePenalty() => defensePenalty;
    public float GetDamagePerMissingHealthPercent() => damagePerMissingHealthPercent;
}


