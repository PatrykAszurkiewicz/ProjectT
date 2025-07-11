using UnityEngine;

public class GetHitButton : MonoBehaviour
{
    PlayerStats pStats;
    public float dmg = 20f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        pStats = FindAnyObjectByType<PlayerStats>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void Hit()
    {
        pStats.TakeDamage(dmg);
    }
}
