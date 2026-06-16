using System.Collections.Generic;
using UnityEngine;

// PROJECTILE PARRY  (Augment 325 — "Unlock Projectile Parry")
//   - Melee parry  : keyed to the ENEMY's attack animation frames + the enemy's
//                     position (ShieldSystem.TryBlockOrParry / IsInParryWindow).
//   - Projectile   : keyed to the PROJECTILE in flight — its proximity drives a
//     parry           reaction window

public static class ProjectileParry
{
    // Per-player unlock (augment 325). Absent index = locked. In co-op each
    // player only gains projectile parry if THEY picked 325.
    private static readonly HashSet<int> _unlocked = new HashSet<int>();

    // Back-compat global flag. Reading = "any player has it"; setting true =
    // unlock player 0 (single-player path). Kept so older callers still compile.
    public static bool Unlocked
    {
        get => _unlocked.Count > 0;
        set { if (value) _unlocked.Add(0); else _unlocked.Clear(); }
    }

    public static bool UnlockedFor(int playerIndex) => _unlocked.Contains(playerIndex);

    public static void SetUnlocked(int playerIndex, bool unlocked = true)
    {
        if (unlocked) _unlocked.Add(playerIndex);
        else _unlocked.Remove(playerIndex);
    }

    /// Clear all players' unlock state (call on a fresh run).
    public static void Reset() => _unlocked.Clear();

    // Co-op resolve: the alive, shield-equipped player NEAREST to `nearPos`
    // (the projectile). Replaces the old single FindGameObjectWithTag("Player")
    // lookup so whichever player is closest to the shot is the one who can parry
    // it, and reports that player's index so the right unlock + parry upgrades apply.
    public static bool TryResolve(Vector3 nearPos,
                                  out ShieldSystem shield,
                                  out Transform playerTransform,
                                  out int playerIndex)
    {
        shield = null;
        playerTransform = null;
        playerIndex = 0;

        var reg = PlayerRegistry.Instance;
        if (reg == null) return false;

        float bestSqr = float.PositiveInfinity;
        var all = reg.All;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (p == null || p.Stats == null || p.Stats.IsDead()) continue;

            var weapon = p.Stats.GetComponentInChildren<Weapon>();
            if (weapon == null) continue;

            var sh = weapon.GetShieldSystem();
            if (sh == null) continue;

            float d = ((Vector2)p.transform.position - (Vector2)nearPos).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                shield = sh;
                playerTransform = p.transform;
                playerIndex = p.PlayerIndex;
            }
        }

        return shield != null;
    }

    // Back-compat (pre-co-op signature). Resolves the first alive shielded player
    // — in single player that's the only player. Any projectile script not yet
    // updated keeps compiling; converted ones use the index-aware overload above.
    public static bool TryResolve(ref Weapon cachedWeapon,
                                  out ShieldSystem shield,
                                  out Transform playerTransform)
    {
        shield = null;
        playerTransform = null;

        var reg = PlayerRegistry.Instance;
        if (reg == null) return false;

        var all = reg.All;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (p == null || p.Stats == null || p.Stats.IsDead()) continue;

            var w = p.Stats.GetComponentInChildren<Weapon>();
            if (w == null) continue;

            var sh = w.GetShieldSystem();
            if (sh == null) continue;

            cachedWeapon = w;
            shield = sh;
            playerTransform = p.transform;
            return true;
        }

        return false;
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

