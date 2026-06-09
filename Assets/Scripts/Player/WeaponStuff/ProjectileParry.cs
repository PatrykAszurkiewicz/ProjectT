using UnityEngine;

// PROJECTILE PARRY  (Augment 325 — "Unlock Projectile Parry")
//   - Melee parry  : keyed to the ENEMY's attack animation frames + the enemy's
//                     position (ShieldSystem.TryBlockOrParry / IsInParryWindow).
//   - Projectile   : keyed to the PROJECTILE in flight — its proximity drives a
//     parry           reaction window

public static class ProjectileParry
{
    // Flipped on by AugmentEffectHandler when augment 325 is applied.
    // Static so any in-flight projectile can read it without a scene lookup.
    public static bool Unlocked = false;

    // Resolves the player's currently-equipped ShieldSystem (if any) and the
    // player transform, caching the Weapon component so projectiles don't spam
    // FindGameObjectWithTag every frame.
    public static bool TryResolve(ref Weapon cachedWeapon,
                                  out ShieldSystem shield,
                                  out Transform playerTransform)
    {
        shield = null;
        playerTransform = null;

        if (cachedWeapon == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                cachedWeapon = player.GetComponentInChildren<Weapon>();
        }

        if (cachedWeapon == null) return false;

        shield = cachedWeapon.GetShieldSystem();

        PlayerStats ps = cachedWeapon.GetComponentInParent<PlayerStats>();
        playerTransform = ps != null ? ps.transform : cachedWeapon.transform;

        return shield != null;
    }
}

// PROJECTILE PARRY INDICATOR

public class ProjectileParryIndicator : MonoBehaviour
{
    private SpriteRenderer exclSR;
    private SpriteRenderer glowSR;
    private float yOffset = 0.55f;
    private float size = 0.45f;
    private float born;

    private static Sprite _excl;
    private static Sprite _glow;

    public static ProjectileParryIndicator Attach(Transform parent, float yOffset = 0.55f, float size = 0.45f)
    {
        GameObject go = new GameObject("ProjectileParryIndicator");
        go.transform.SetParent(parent, false);
        var ind = go.AddComponent<ProjectileParryIndicator>();
        ind.yOffset = yOffset;
        ind.size = size;
        ind.Build();
        return ind;
    }

    private void Build()
    {
        born = Time.time;

        GameObject glowGO = new GameObject("Glow");
        glowGO.transform.SetParent(transform, false);
        glowGO.transform.localPosition = new Vector3(0f, yOffset, 0f);
        glowGO.transform.localScale = Vector3.one * (size * 2.2f);
        glowSR = glowGO.AddComponent<SpriteRenderer>();
        glowSR.sprite = GetGlow();
        glowSR.color = new Color(0.6f, 0.85f, 1f, 0.35f);
        glowSR.sortingOrder = 9600;

        GameObject exclGO = new GameObject("Excl");
        exclGO.transform.SetParent(transform, false);
        exclGO.transform.localPosition = new Vector3(0f, yOffset, 0f);
        exclGO.transform.localScale = Vector3.one * size;
        exclSR = exclGO.AddComponent<SpriteRenderer>();
        exclSR.sprite = GetExcl();
        exclSR.color = new Color(0.7f, 0.92f, 1f, 1f);
        exclSR.sortingOrder = 9601;
    }

    private void LateUpdate()
    {
        // Keep upright (cancel any parent rotation from the projectile facing
        // its travel direction) and bob/pulse for visibility.
        transform.rotation = Quaternion.identity;

        float elapsed = Time.time - born;
        float bob = Mathf.Sin(elapsed * 12f) * 0.04f;
        if (exclSR != null)
            exclSR.transform.localPosition = new Vector3(0f, yOffset + bob, 0f);
        if (glowSR != null)
        {
            float pulse = 0.25f + Mathf.PingPong(elapsed * 3f, 0.25f);
            Color c = glowSR.color; c.a = pulse; glowSR.color = c;
            glowSR.transform.localPosition = new Vector3(0f, yOffset + bob, 0f);
        }
    }

    private static Sprite GetExcl()
    {
        if (_excl != null) return _excl;
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var px = new Color[S * S];
        for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

        // Vertical bar
        for (int y = 10; y <= 28; y++)
            for (int x = 13; x <= 18; x++)
                px[y * S + x] = Color.white;
        // Dot
        for (int y = 4; y <= 8; y++)
            for (int x = 13; x <= 18; x++)
                px[y * S + x] = Color.white;

        tex.SetPixels(px);
        tex.Apply();
        _excl = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _excl;
    }

    private static Sprite GetGlow()
    {
        if (_glow != null) return _glow;
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / (S * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                px[y * S + x] = new Color(1f, 1f, 1f, a * a * a);
            }
        tex.SetPixels(px);
        tex.Apply();
        _glow = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _glow;
    }
}
