using UnityEngine;

public class DamageBoostAugment : AugmentEffect
{
    public float mult = 1.5f;

    public override void Apply()
    {
        var player = GameObject.FindAnyObjectByType<PlayerStats>();
        if(player != null)
        {
            // rest of code
        }
    }
}
