using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class FriendlyFireEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float infightingChance = 1.3f;
    [System.NonSerialized]
    public float infightingDuration = 10000f;

    [Header("Timers")]
    private float cooldownTimer = 0f;
    private float cooldownDuration = 30f;
    private bool triggerPending = false;

    private void Start()
    {
        //Debug.Log($"[FRIENDLY_FIRE] Effect started Chance: {infightingChance * 100f:F1}%, Duration: {infightingDuration}s, Cooldown: {cooldownDuration}s");
        cooldownTimer = cooldownDuration - 1f;
    }

    private void Update()
    {
        // If we have a pending trigger, keep checking for enemies
        if (triggerPending)
        {
            TryTriggerFriendlyFire();
            return; // Don't increment cooldown while waiting
        }

        cooldownTimer += Time.deltaTime;

        if (cooldownTimer >= cooldownDuration)
        {
            cooldownTimer = 0f;
            //Debug.Log("[FRIENDLY_FIRE] Cooldown expired, checking for trigger");

            // Roll for chance first
            float roll = UnityEngine.Random.value;
            //Debug.Log($"[FRIENDLY_FIRE] Rolled {roll:F2} against chance {infightingChance:F2}");

            if (roll <= infightingChance)
            {
                triggerPending = true; // Mark that we want to trigger
                //Debug.Log("[FRIENDLY_FIRE] Trigger SUCCESS - waiting for enemies");
            }
            else
            {
                //Debug.Log("[FRIENDLY_FIRE] Roll failed, no trigger this time");
            }
        }
    }

    private void TryTriggerFriendlyFire()
    {
        var allEnemies = FindObjectsByType<EnemyStats>(FindObjectsSortMode.None)
            .Where(e => e != null &&
                        !e.IsDead() &&
                        e.GetComponent<BerserkEnemy>() == null &&
                        e.GetComponent<GremlinController>() == null) // EXCLUDE GREMLINS
            .ToList();

        //Debug.Log($"[FRIENDLY_FIRE] Found {allEnemies.Count} eligible enemies (gremlins excluded)");

        if (allEnemies.Count < 2)
        {
            return;
        }

        triggerPending = false;

        EnemyStats chosenEnemy = allEnemies[UnityEngine.Random.Range(0, allEnemies.Count)];

        //Debug.Log($"[FRIENDLY_FIRE]  TRIGGERED {chosenEnemy.gameObject.name} going berserk for {infightingDuration}s ★★★");
        MakeEnemyBerserk(chosenEnemy);
    }

    private void MakeEnemyBerserk(EnemyStats enemy)
    {
        if (enemy == null) return;

        var berserk = enemy.gameObject.AddComponent<BerserkEnemy>();
        berserk.Initialize(infightingDuration);
    }

    [ContextMenu("Force Trigger Now")]
    public void ForceTrigger()
    {
        //Debug.Log("[FRIENDLY_FIRE] FORCE TRIGGER - Manual test");
        triggerPending = true;
        infightingChance = 1.0f;
    }
}