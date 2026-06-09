using UnityEngine;

// Augment 78 (Increased Shield Defenses): flat percentage reduction to incoming
// player damage. Stacks additively across applications, capped below 100%.
public class PlayerDamageReductionEffect : MonoBehaviour
{
    [Tooltip("Fraction of incoming damage removed. 0.15 = -15% damage taken.")]
    public float damageReductionPercent = 0f;

    // Returns the damage left after reduction.
    public float Apply(float incomingDamage)
    {
        float reduction = Mathf.Clamp(damageReductionPercent, 0f, 0.95f);
        return incomingDamage * (1f - reduction);
    }
}


