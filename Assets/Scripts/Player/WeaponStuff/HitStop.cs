using UnityEngine;
using System.Collections;
using System.Collections.Generic;


// Brief freeze-frame on impact.
// DOES NOT USE Time.timeScale — that breaks WaitForSeconds everywhere.
// Instead, notifies subscribers to pause their movement for a few frames.
// Enemies and the player opt-in by checking HitStop.IsFrozen in their movement code.

public class HitStop : MonoBehaviour
{
    public static HitStop Instance { get; private set; }


    // Check this in FixedUpdate to skip movement during hitstop.
    // Does NOT affect coroutines, cooldowns, or animations.

    public static bool IsFrozen { get; private set; }

    [Header("Defaults")]
    [SerializeField] private float defaultDuration = 0.065f;
    [SerializeField] private float cooldown = 0.08f;

    private float lastFreezeTime = -Mathf.Infinity;
    private Coroutine freezeCoroutine;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            IsFrozen = false;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void Freeze(float duration = -1f, bool ignoreCooldown = false)
    {
        if (duration < 0f) duration = defaultDuration;

        if (!ignoreCooldown && Time.unscaledTime - lastFreezeTime < cooldown)
            return;

        lastFreezeTime = Time.unscaledTime;

        if (freezeCoroutine != null)
            StopCoroutine(freezeCoroutine);

        freezeCoroutine = StartCoroutine(FreezeRoutine(duration));
    }

    public void FreezeOLD(float duration = -1f)
    {
        if (duration < 0f) duration = defaultDuration;

        if (Time.unscaledTime - lastFreezeTime < cooldown)
            return;

        lastFreezeTime = Time.unscaledTime;

        if (freezeCoroutine != null)
            StopCoroutine(freezeCoroutine);

        freezeCoroutine = StartCoroutine(FreezeRoutine(duration));
    }

    private IEnumerator FreezeRoutine(float duration)
    {
        IsFrozen = true;

        // Count frames using unscaled delta so this works regardless of timeScale
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null; // waits one frame, not affected by freeze
        }

        IsFrozen = false;
        freezeCoroutine = null;
    }

    void OnDestroy()
    {
        IsFrozen = false;
    }
}

