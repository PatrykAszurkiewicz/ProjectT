using UnityEngine;
using System;
using System.Collections;

public class CharacterStats : MonoBehaviour
{
    [Header("HP")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;
    public float currentArmor = 0f;

    public event Action<float, float> OnHealthChanged;
    // parametry: currentHealth, maxHealth

    // Animated capacity change support 
    // Tracks the running animation so a second augment pick replaces a still-tweening one cleanly.
    private Coroutine _maxHealthTween;

    [Header("Capacity Change Animation")]
    [Tooltip("Duration of the smooth fill-up tween when maxHealth increases (e.g. from an augment). The bar grows visibly from its current ratio up to full over this time.")]
    public float maxHealthIncreaseAnimDuration = 0.6f;

    [Tooltip("Minimum visible motion (as a fraction of maxHealth) for the bar when an augment is picked. If the natural fill-up is smaller than this — e.g. the player was already at full HP — we briefly dip currentHealth by this much so the player sees a clear 'pulse' instead of nothing. Keep small (~0.12) so the dip reads as a pulse, not damage.")]
    [Range(0f, 0.5f)]
    public float maxHealthMinPulseFraction = 0.12f;

    public virtual void TakeDamage(float amount)
    {
        float mitigated = Mathf.Max(amount - currentArmor, 0f);
        currentHealth -= mitigated;
        currentHealth = Mathf.Max(currentHealth, 0f);

        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (IsDead())
        {
            Die();
        }
    }

    public virtual void Heal(float amount)
    {
        currentHealth += amount;
        currentHealth = Mathf.Min(currentHealth, maxHealth);

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public virtual void Die()
    {
        Destroy(gameObject);
    }

    public virtual bool IsDead()
    {
        return currentHealth <= 0;
    }

    // Allows derived classes to trigger health changed event
    protected void TriggerHealthChangedEvent()
    {
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    //  Animated maxHealth change
    // Use this instead of writing maxHealth directly when an augment / buff
    // changes the cap at runtime. It keeps currentHealth, the UI event, and
    // the visible bar in sync — and on increases gives a satisfying, clearly-
    // visible fill animation rather than a silent change followed by a
    // delayed snap (the augment-32 bug).

    public void SetMaxHealthAnimated(float newMax)
    {
        if (newMax <= 0f || float.IsNaN(newMax) || float.IsInfinity(newMax))
        {
            Debug.LogWarning($"[CharacterStats] SetMaxHealthAnimated ignored invalid value: {newMax}");
            return;
        }

        float oldMax = maxHealth;

        // Cap shrunk: clamp current, fire event, done.
        if (newMax <= oldMax)
        {
            maxHealth = newMax;
            if (currentHealth > maxHealth) currentHealth = maxHealth;
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            return;
        }

        // Cap grew. Preserve fill ratio across the cap change so the bar
        // doesn't initially perceive the moment as damage.
        float ratio = (oldMax > 0f) ? Mathf.Clamp01(currentHealth / oldMax) : 1f;
        maxHealth = newMax;
        currentHealth = maxHealth * ratio;

        // Cancel any in-flight tween cleanly — new pickup, new tween.
        if (_maxHealthTween != null)
        {
            StopCoroutine(_maxHealthTween);
            _maxHealthTween = null;
        }

        // Bail-outs for animation, but ALWAYS fire the event so the bar at
        // least snaps to the right ratio.
        if (IsDead() || !isActiveAndEnabled)
        {
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            return;
        }

        // Guarantee a minimum visible animation distance by dipping currentHealth
        // if the natural fill-up is too small to see. The dip is small enough to
        // read as a "pulse" rather than damage.
        float naturalGapFraction = (maxHealth - currentHealth) / maxHealth; // 0 when full
        float minGapFraction = Mathf.Clamp01(maxHealthMinPulseFraction);
        if (naturalGapFraction < minGapFraction)
        {
            currentHealth = maxHealth * (1f - minGapFraction);
        }

        // Fire the event AFTER the dip so the UI catches the start state of
        // the tween. The tween then ramps current back up monotonically.
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth < maxHealth)
        {
            _maxHealthTween = StartCoroutine(AnimateMaxHealthFillUp());
        }
    }

    private IEnumerator AnimateMaxHealthFillUp()
    {
        float startHealth = currentHealth;
        float targetHealth = maxHealth;
        float duration = Mathf.Max(0.05f, maxHealthIncreaseAnimDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // If currentHealth was lowered externally (damage taken) during the
            // tween, abort so we don't fight TakeDamage / Heal.
            if (currentHealth < startHealth - 0.01f) { _maxHealthTween = null; yield break; }
            // If maxHealth changed again mid-tween (another augment),
            // SetMaxHealthAnimated already started a fresh tween — abort.
            if (!Mathf.Approximately(maxHealth, targetHealth)) { _maxHealthTween = null; yield break; }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Quadratic ease-out: fast start, gentle settle at full.
            float eased = 1f - (1f - t) * (1f - t);
            currentHealth = Mathf.Lerp(startHealth, targetHealth, eased);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            yield return null;
        }

        currentHealth = targetHealth;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        _maxHealthTween = null;
    }

}
