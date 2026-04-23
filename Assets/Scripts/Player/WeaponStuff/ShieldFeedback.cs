using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// SHIELD FEEDBACK SYSTEM
// Provides visual + audio feedback for shield blocks and parries.
// Called from ShieldSystem.TryBlockOrParry() after determining block vs parry.

public static class ShieldFeedback
{
    // ── Tuning ──

    // Block
    private const float BLOCK_SHAKE_INTENSITY = 0.15f;
    private const float BLOCK_SHAKE_DURATION = 0.10f;
    private const int BLOCK_SPARK_COUNT = 5;
    private const float BLOCK_SPARK_SPEED = 3.5f;
    private const float BLOCK_SPARK_LIFETIME = 0.2f;

    // Parry
    private const float PARRY_SHAKE_INTENSITY = 0.30f;
    private const float PARRY_SHAKE_DURATION = 0.18f;
    private const float PARRY_HITSTOP_DURATION = 0.07f;
    private const float PARRY_FLASH_DURATION = 0.08f;

    // Call when the shield successfully BLOCKS an attack (not a parry).
    public static void OnBlock(Transform playerTransform, Vector3 attackerPosition, LineRenderer arcLine)
    {
        if (playerTransform == null) return;

        //  Camera shake

        if (CombatFeelManager.Instance != null)
            CombatFeelManager.Instance.DoShake(BLOCK_SHAKE_INTENSITY, BLOCK_SHAKE_DURATION);

        //  Shield arc flash and radius pulse 
        if (arcLine != null)
        {
            var host = arcLine.GetComponent<ShieldFeedbackHost>();
            if (host == null)
                host = arcLine.gameObject.AddComponent<ShieldFeedbackHost>();
            host.FlashArc(arcLine, playerTransform);
        }

        //  Impact sparks at the contact point 
        Vector3 contactDir = (attackerPosition - playerTransform.position).normalized;
        Vector3 contactPoint = playerTransform.position + contactDir * 0.9f;
        SpawnBlockSparks(contactPoint, contactDir);

        //  Audio placeholder 
        // TODO: Uncomment when FMOD shieldBlock event is ready
        // if (AudioManager.instance != null && FMODEvents.instance != null)
        //     AudioManager.instance.PlayOneShot(FMODEvents.instance.shieldBlock, playerTransform.position);
    }


    // Call when the shield successfully PARRIES an attack (perfect timing).
    public static void OnParry(Transform playerTransform, Vector3 attackerPosition)
    {
        if (playerTransform == null) return;

        // ── Camera shake (punchy) ──
        if (CombatFeelManager.Instance != null)
            CombatFeelManager.Instance.DoShake(PARRY_SHAKE_INTENSITY, PARRY_SHAKE_DURATION);

        // ── Hitstop (brief freeze frame for impact) ──
        if (HitStop.Instance != null)
            HitStop.Instance.Freeze(PARRY_HITSTOP_DURATION);

        // ── Player sprite white flash ──
        SpriteRenderer playerSR = playerTransform.GetComponent<SpriteRenderer>();
        if (playerSR != null)
        {
            var host = playerTransform.GetComponent<ShieldFeedbackHost>();
            if (host == null)
                host = playerTransform.gameObject.AddComponent<ShieldFeedbackHost>();
            host.FlashSprite(playerSR, PARRY_FLASH_DURATION);
        }

        //  Audio placeholder 
        // TODO: Uncomment when FMOD shieldParry event is ready
        // if (AudioManager.instance != null && FMODEvents.instance != null)
        //     AudioManager.instance.PlayOneShot(FMODEvents.instance.shieldParry, playerTransform.position);
    }


    //  Spark VFX 

    private static void SpawnBlockSparks(Vector3 origin, Vector3 attackDir)
    {
        GameObject host = new GameObject("BlockSparks");
        host.transform.position = origin;
        var sparks = host.AddComponent<BlockSparksVFX>();
        sparks.Initialize(attackDir, BLOCK_SPARK_COUNT, BLOCK_SPARK_SPEED, BLOCK_SPARK_LIFETIME);
    }


}


// SHIELD FEEDBACK HOST
// Lightweight MonoBehaviour for coroutine-based effects on existing GameObjects


public class ShieldFeedbackHost : MonoBehaviour
{
    private Coroutine arcFlashRoutine;
    private Coroutine spriteFlashRoutine;

    // Briefly brightens and pulses the shield arc LineRenderer outward.
    public void FlashArc(LineRenderer arcLine, Transform playerTransform)
    {
        if (arcFlashRoutine != null) StopCoroutine(arcFlashRoutine);
        arcFlashRoutine = StartCoroutine(ArcFlashRoutine(arcLine, playerTransform));
    }

    /// Briefly flashes a SpriteRenderer white.
    public void FlashSprite(SpriteRenderer sr, float duration)
    {
        if (spriteFlashRoutine != null) StopCoroutine(spriteFlashRoutine);
        spriteFlashRoutine = StartCoroutine(SpriteFlashRoutine(sr, duration));
    }

    private IEnumerator ArcFlashRoutine(LineRenderer arcLine, Transform playerTransform)
    {
        if (arcLine == null || playerTransform == null) yield break;

        Color originalStart = arcLine.startColor;
        Color originalEnd = arcLine.endColor;
        float originalWidth = arcLine.startWidth;

        // Snapshot original arc positions
        int pointCount = arcLine.positionCount;
        Vector3[] originalPositions = new Vector3[pointCount];
        arcLine.GetPositions(originalPositions);

        // Compute pushed-out positions (gently expand arc radius from player center)
        Vector3 center = playerTransform.position;
        float pulseScale = 1.12f;
        Vector3[] pushedPositions = new Vector3[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            Vector3 offset = originalPositions[i] - center;
            pushedPositions[i] = center + offset * pulseScale;
        }

        // Instant flash: bright color + wider line + slightly pushed-out arc
        Color flashColor = new Color(1f, 1f, 1f, 0.9f);
        arcLine.startColor = flashColor;
        arcLine.endColor = flashColor;
        arcLine.startWidth = originalWidth * 1.8f;
        arcLine.endWidth = originalWidth * 1.8f;
        arcLine.SetPositions(pushedPositions);

        yield return new WaitForSeconds(0.06f);

        // Smooth settle back to original
        float fadeDuration = 0.12f;
        float elapsed = 0f;
        Vector3[] lerpPositions = new Vector3[pointCount];

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            float smooth = t * t * (3f - 2f * t); // smoothstep

            arcLine.startColor = Color.Lerp(flashColor, originalStart, smooth);
            arcLine.endColor = Color.Lerp(flashColor, originalEnd, smooth);
            float w = Mathf.Lerp(originalWidth * 1.8f, originalWidth, smooth);
            arcLine.startWidth = w;
            arcLine.endWidth = w;

            // Lerp positions back to original
            for (int i = 0; i < pointCount; i++)
                lerpPositions[i] = Vector3.Lerp(pushedPositions[i], originalPositions[i], smooth);
            arcLine.SetPositions(lerpPositions);

            yield return null;
        }

        // Ensure clean restore
        arcLine.startColor = originalStart;
        arcLine.endColor = originalEnd;
        arcLine.startWidth = originalWidth;
        arcLine.endWidth = originalWidth;
        // Don't force-set positions here — ShieldSystem.Update() is already recalculating them every frame, so they'll snap back naturally.
        arcFlashRoutine = null;
    }

    private IEnumerator SpriteFlashRoutine(SpriteRenderer sr, float duration)
    {
        if (sr == null) yield break;

        Color original = sr.color;

        // Hard white
        sr.color = Color.white;
        yield return new WaitForSeconds(duration * 0.5f);

        // Warm tint (slight gold for parry "success" feel)
        sr.color = new Color(1f, 0.95f, 0.7f, 1f);
        yield return new WaitForSeconds(duration * 0.5f);

        sr.color = original;
        spriteFlashRoutine = null;
    }
}


// BLOCK SPARKS VFX
// Small bright particles that burst outward from the contact point on block.


public class BlockSparksVFX : MonoBehaviour
{
    private struct Spark
    {
        public SpriteRenderer renderer;
        public Vector2 velocity;
    }

    private Spark[] sparks;
    private float lifetime;
    private float elapsed;

    private static Sprite _sparkSprite;

    public void Initialize(Vector3 attackDir, int count, float speed, float life)
    {
        lifetime = life;
        elapsed = 0f;
        sparks = new Spark[count];

        Sprite sprite = GetSparkSprite();

        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject($"Spark_{i}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 9500;

            // Sparks fly in a cone opposite the attack direction (reflecting off the shield)
            float spreadAngle = Random.Range(-60f, 60f);
            Vector2 reflectDir = -new Vector2(attackDir.x, attackDir.y);
            float baseAngle = Mathf.Atan2(reflectDir.y, reflectDir.x) * Mathf.Rad2Deg;
            float finalAngle = (baseAngle + spreadAngle) * Mathf.Deg2Rad;

            Vector2 vel = new Vector2(Mathf.Cos(finalAngle), Mathf.Sin(finalAngle))
                          * speed * Random.Range(0.6f, 1.2f);

            // Color: mix of white, yellow, light blue for metallic impact
            Color[] palette = {
                new Color(1f, 1f, 1f, 1f),
                new Color(1f, 0.95f, 0.6f, 1f),
                new Color(0.7f, 0.85f, 1f, 1f)
            };
            sr.color = palette[i % palette.Length];

            float scale = Random.Range(0.08f, 0.18f);
            go.transform.localScale = Vector3.one * scale;

            sparks[i] = new Spark { renderer = sr, velocity = vel };
        }
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = elapsed / lifetime;

        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        for (int i = 0; i < sparks.Length; i++)
        {
            if (sparks[i].renderer == null) continue;

            // Move
            sparks[i].renderer.transform.localPosition +=
                (Vector3)sparks[i].velocity * Time.deltaTime;

            // Decelerate
            sparks[i].velocity *= 0.92f;

            // Shrink and fade
            float scale = Mathf.Lerp(0.18f, 0f, t);
            sparks[i].renderer.transform.localScale = Vector3.one * scale;

            Color c = sparks[i].renderer.color;
            c.a = 1f - t;
            sparks[i].renderer.color = c;
        }
    }

    private static Sprite GetSparkSprite()
    {
        if (_sparkSprite != null) return _sparkSprite;

        const int S = 6;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[S * S];
        Vector2 center = new Vector2(S * 0.5f, S * 0.5f);

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center) / (S * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                px[y * S + x] = new Color(1f, 1f, 1f, a * a);
            }

        tex.SetPixels(px);
        tex.Apply();
        _sparkSprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _sparkSprite;
    }
}
