using UnityEngine;
using System.Collections;

public class CoreShieldMatrix : MonoBehaviour
{
    [System.NonSerialized]
    public float maxShieldStrength = 200f;
    [System.NonSerialized]
    public float currentShieldStrength = 200f;

    [Header("Shield Visual Settings")]
    public Color shieldColor = new Color(0.3f, 0.7f, 1f, 0.6f); // Cyan shield
    public float shieldPulseSpeed = 2f;
    public float shieldScaleMultiplier = 0.46f;

    private CentralCore core;
    private GameObject shieldVisual;
    private SpriteRenderer shieldRenderer;
    private Material shieldMaterial;
    private bool isShieldActive = true;
    private Coroutine rechargeCoroutine;

    // TODO - add shield recharge mechanics 
    public bool enableShieldRecharge = false;
    public float rechargeDelay = 5f; // Seconds before recharge starts
    public float rechargeRate = 50f; // HP per second

    void Awake()
    {
        core = GetComponent<CentralCore>();
        if (core == null)
        {
            Debug.LogError("[CORE_SHIELD] CoreShieldMatrix requires CentralCore component");
            enabled = false;
            return;
        }

        currentShieldStrength = maxShieldStrength;
    }

    void Start()
    {
        CreateShieldVisual();

        // Subscribe to core damage events to intercept damage
        core.OnDamageTaken += OnCoreDamaged;

        //Debug.Log($"[CORE_SHIELD] Shield activated with {maxShieldStrength} HP");
    }

    void OnDestroy()
    {
        if (core != null)
        {
            core.OnDamageTaken -= OnCoreDamaged;
        }

        if (shieldVisual != null)
        {
            Destroy(shieldVisual);
        }

        if (rechargeCoroutine != null)
        {
            StopCoroutine(rechargeCoroutine);
        }
    }

    void CreateShieldVisual()
    {
        shieldVisual = new GameObject("ShieldEffect");
        shieldVisual.transform.SetParent(transform);
        shieldVisual.transform.localPosition = Vector3.zero;

        shieldRenderer = shieldVisual.AddComponent<SpriteRenderer>();
        shieldRenderer.sprite = CreateShieldSprite();
        shieldRenderer.color = shieldColor;
        shieldRenderer.sortingOrder = core.GetComponent<SpriteRenderer>().sortingOrder + 1;

        float coreSize = core.coreSize;
        shieldVisual.transform.localScale = Vector3.one * coreSize * shieldScaleMultiplier;

        // Create a glowing material for the shield
        shieldMaterial = new Material(Shader.Find("Sprites/Default"));
        shieldRenderer.material = shieldMaterial;

        StartCoroutine(ShieldPulseEffect());
    }

    Sprite CreateShieldSprite()
    {
        int size = 128;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float outerRadius = size * 0.45f;
        float innerRadius = size * 0.35f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                Vector2 pos = new Vector2(x, y);
                float distance = Vector2.Distance(pos, center);

                if (distance <= outerRadius && distance >= innerRadius)
                {
                    // Create a hexagonal shield pattern
                    float angle = Mathf.Atan2(pos.y - center.y, pos.x - center.x);
                    float hexPattern = Mathf.Cos(angle * 6f) * 0.5f + 0.5f;

                    // Smooth falloff
                    float alpha = 1f - Mathf.Abs(distance - (innerRadius + outerRadius) * 0.5f) / ((outerRadius - innerRadius) * 0.5f);
                    alpha = Mathf.Clamp01(alpha) * hexPattern;

                    colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
                else
                {
                    colors[y * size + x] = Color.clear;
                }
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    IEnumerator ShieldPulseEffect()
    {
        while (isShieldActive && currentShieldStrength > 0)
        {
            float time = Time.time * shieldPulseSpeed;
            float pulse = Mathf.Sin(time) * 0.5f + 0.5f;

            if (shieldRenderer != null)
            {
                Color pulsedColor = shieldColor;
                pulsedColor.a = shieldColor.a * (0.4f + pulse * 0.6f);
                shieldRenderer.color = pulsedColor;
            }

            yield return null;
        }
    }

    // This is called after the core has already taken damage
    private void OnCoreDamaged(float damage, GameObject source)
    {
        // This event fires after damage is taken
    }

    // Public method that should be called before the central core takes damage
    public bool AbsorbDamage(ref float damageAmount)
    {
        if (currentShieldStrength <= 0)
        {
            return false; // Shield is depleted, damage goes through
        }

        float damageToAbsorb = Mathf.Min(damageAmount, currentShieldStrength);
        currentShieldStrength -= damageToAbsorb;
        damageAmount -= damageToAbsorb;

        //Debug.Log($"[CORE_SHIELD] Absorbed {damageToAbsorb:F1} damage. Shield: {currentShieldStrength:F1}/{maxShieldStrength:F1}");

        // Visual feedback for shield hit
        StartCoroutine(ShieldHitEffect());

        // Play shield hit sound
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.towerDamage, transform.position);
        }

        if (currentShieldStrength <= 0)
        {
            OnShieldDepleted();
        }
        else if (enableShieldRecharge)
        {
            // Restart recharge timer
            if (rechargeCoroutine != null)
            {
                StopCoroutine(rechargeCoroutine);
            }
            rechargeCoroutine = StartCoroutine(RechargeShield());
        }

        return damageAmount <= 0; // Return true if all damage was absorbed
    }

    IEnumerator ShieldHitEffect()
    {
        if (shieldRenderer == null) yield break;

        Color originalColor = shieldRenderer.color;
        Color hitColor = Color.white;

        shieldRenderer.color = hitColor;
        yield return new WaitForSeconds(0.1f);
        shieldRenderer.color = originalColor;
    }

    void OnShieldDepleted()
    {
        //Debug.Log("[CORE_SHIELD] Shield depleted!");

        if (shieldVisual != null)
        {
            StartCoroutine(ShieldBreakEffect());
        }

        // Play shield break sound
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.towerDeath, transform.position);
        }
    }

    IEnumerator ShieldBreakEffect()
    {
        if (shieldRenderer == null) yield break;

        float duration = 0.5f;
        float elapsed = 0f;
        Color startColor = shieldRenderer.color;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // Fade out and scale up
            Color currentColor = startColor;
            currentColor.a = Mathf.Lerp(startColor.a, 0f, t);
            shieldRenderer.color = currentColor;

            float scale = Mathf.Lerp(1f, 1.5f, t);
            shieldVisual.transform.localScale = Vector3.one * core.coreSize * shieldScaleMultiplier * scale;

            yield return null;
        }

        shieldVisual.SetActive(false);
    }

    // TODO implement recharging of shield
    IEnumerator RechargeShield()
    {
        yield return new WaitForSeconds(rechargeDelay);

        //Debug.Log("[CORE_SHIELD] Shield recharging...");

        while (currentShieldStrength < maxShieldStrength)
        {
            currentShieldStrength = Mathf.Min(currentShieldStrength + rechargeRate * Time.deltaTime, maxShieldStrength);

            // Update shield visual opacity based on strength
            if (shieldRenderer != null)
            {
                float strengthPercent = currentShieldStrength / maxShieldStrength;
                Color currentColor = shieldColor;
                currentColor.a = shieldColor.a * strengthPercent;
                shieldRenderer.color = currentColor;
            }

            // Reactivate shield visual if it was deactivated
            if (!shieldVisual.activeSelf && currentShieldStrength > 0)
            {
                shieldVisual.SetActive(true);
                shieldVisual.transform.localScale = Vector3.one * core.coreSize * shieldScaleMultiplier;
            }

            yield return null;
        }

        //Debug.Log("[CORE_SHIELD] Shield fully recharged!");
        rechargeCoroutine = null;
    }

    public float GetShieldStrength() => currentShieldStrength;
    public float GetMaxShieldStrength() => maxShieldStrength;
    public float GetShieldPercentage() => maxShieldStrength > 0 ? currentShieldStrength / maxShieldStrength : 0f;
    public bool IsShieldActive() => currentShieldStrength > 0;
}
