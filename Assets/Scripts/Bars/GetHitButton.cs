using UnityEngine;

public class GetHitButton : MonoBehaviour
{
    public PlayerStats playerStats;
    public float dmg = 20f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void Hit()
    {
        playerStats.TakeDamage(dmg);
    }
}
