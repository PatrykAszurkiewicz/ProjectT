using UnityEngine;
using System.Collections.Generic;

/// Drives the Stealth Cloak's invisibility state on the PLAYER.
///   • StealthCloakSystem.Activate() calls TryActivate() on right-click.
///   • A second right-click while cloaked calls Deactivate() (early uncloak).
///   • While active the player's sprite goes semi-transparent and a soft
///     shimmer / refraction ring orbits the player.
///   • Invisibility ends when ANY of: the duration runs out, the player
///     attacks an enemy/boss (NotifyPlayerAttacked()), or the player
///     right-clicks again (Deactivate()).
///   • After it ends a cooldown must elapse before it can be re-activated.
/// "Enemies ignore the player" is implemented via the static isActive
/// flag, which EnemyController.UpdateTarget() and Boss1.FindTarget() consult.
/// This component is the single source of truth for that flag.

public class PlayerCloakEffect : MonoBehaviour
{
    //  Static query surface (read by EnemyController / Boss1) 
    private static PlayerCloakEffect _instance;

    /// True while the player is currently invisible. Enemies and bosses
    /// must NOT target the player while this is true.
    public static bool IsActive => _instance != null && _instance._isInvisible;

    //  Config (pushed in by StealthCloakSystem from WeaponData) 
    private float duration = 30f;
    private float cooldown = 10f;
    private float playerAlpha = 0.28f;   // sprite opacity while cloaked

    //  Runtime state 
    private bool _isInvisible = false;
    private bool _onCooldown = false;
    private float _invisTimer = 0f;
    private float _cooldownTimer = 0f;

    public bool IsInvisible => _isInvisible;
    public bool IsOnCooldown => _onCooldown;
    public float InvisibilityTimeLeft => _isInvisible ? Mathf.Max(0f, _invisTimer) : 0f;
    /// 1..0 progress of the invisibility duration (1 = just activated, 0 =
    /// about to expire). Rendered as a depleting countdown clock by the UI.
    public float ActiveNormalized => (_isInvisible && duration > 0f)
        ? Mathf.Clamp01(_invisTimer / duration) : 0f;
    public float CooldownTimeLeft => _onCooldown ? Mathf.Max(0f, _cooldownTimer) : 0f;
    /// 0..1 cooldown progress (1 = ready). Handy for a future UI radial.
    public float CooldownNormalized => (cooldown <= 0f || !_onCooldown) ? 1f : 1f - Mathf.Clamp01(_cooldownTimer / cooldown);

    //  Sprite fade 

    private readonly List<SpriteRenderer> _playerRenderers = new List<SpriteRenderer>();
    private readonly List<float> _originalAlphas = new List<float>();

    //  Shimmer VFX 
    private GameObject _vfxRoot;
    private SpriteRenderer[] _shimmerRings;
    private float _vfxTime;
    private const int RING_COUNT = 2;
    private static Sprite _cachedRingSprite;

    private static readonly Color SHIMMER_COLOR = new Color(0.55f, 0.8f, 1f, 1f);

    void Awake()
    {
        // Last-created instance wins; there is only ever one player.
        _instance = this;
    }

    void OnDestroy()
    {
        // Fail safe: never leave the world thinking the player is invisible.
        if (_instance == this)
        {
            RestorePlayerSprites();
            _instance = null;
        }
        DestroyVFX();
    }

    void OnDisable()
    {
        // If the player object is disabled mid-cloak, drop invisibility so
        // enemies don't keep ignoring a player that is about to be re-enabled
        // in an unknown state.
        if (_isInvisible)
            EndInvisibility(playSound: false);
    }

    // Push tuning from WeaponData. Safe to call repeatedly (e.g. on re-equip).
    public void Configure(float duration, float cooldown, float playerAlpha)
    {
        if (duration > 0f) this.duration = duration;
        if (cooldown >= 0f) this.cooldown = cooldown;
        this.playerAlpha = Mathf.Clamp01(playerAlpha);
    }

    // Attempt to go invisible. Returns true if invisibility actually started
    // (false if already invisible or still on cooldown). StealthCloakSystem
    // uses the return value to decide whether to start the tool cooldown.
    public bool TryActivate()
    {
        if (_isInvisible || _onCooldown) return false;

        _isInvisible = true;
        _invisTimer = duration;

        CachePlayerSprites();
        ApplyPlayerAlpha(playerAlpha);
        BuildVFX();

        PlayCloakSound();
        return true;
    }

    // Manual early uncloak (second right-click). No-op if not invisible.
    // Ends invisibility and starts the cooldown, same as a natural expiry.
    public void Deactivate()
    {
        if (!_isInvisible) return;
        EndInvisibility(playSound: true);
    }

    // Called by StealthCloakSystem when the player attacks an enemy or boss.
    // Ends invisibility early. No-op if not currently invisible.
    public void NotifyPlayerAttacked()
    {
        if (!_isInvisible) return;
        EndInvisibility(playSound: true);
    }

    /// <summary>Force-clear everything (used by tool Cleanup on swap).</summary>
    public void ForceClear()
    {
        if (_isInvisible)
            EndInvisibility(playSound: false);
    }

    void Update()
    {
        if (_isInvisible)
        {
            _invisTimer -= Time.deltaTime;

            // Re-assert the cloak alpha every frame. 
            ApplyPlayerAlpha(playerAlpha);

            UpdateVFX();

            if (_invisTimer <= 0f)
                EndInvisibility(playSound: true);
        }
        else if (_onCooldown)
        {
            _cooldownTimer -= Time.deltaTime;
            if (_cooldownTimer <= 0f)
            {
                _cooldownTimer = 0f;
                _onCooldown = false;
            }
        }
    }

    // ── INTERNAL ──

    private void EndInvisibility(bool playSound)
    {
        _isInvisible = false;
        _invisTimer = 0f;

        RestorePlayerSprites();
        DestroyVFX();

        // Start the cooldown only if one is configured.
        if (cooldown > 0f)
        {
            _onCooldown = true;
            _cooldownTimer = cooldown;
        }

        if (playSound)
            PlayUncloakSound();
    }

    //  Player sprite fade 

    private void CachePlayerSprites()
    {
        _playerRenderers.Clear();
        _originalAlphas.Clear();

        // Collect renderers on the player and its children, but EXCLUDE the
        // held weapon visual and any cursor visual so we only fade the body.
        var all = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
        foreach (var sr in all)
        {
            if (sr == null) continue;
            if (sr.GetComponentInParent<Weapon>() != null) continue;       // held weapon
            if (sr.GetComponentInParent<CursorPointer>() != null) continue; // cursor visual

            _playerRenderers.Add(sr);
            _originalAlphas.Add(sr.color.a);
        }

        if (_playerRenderers.Count == 0)
            Debug.LogWarning("[PlayerCloakEffect] No SpriteRenderer found on the " +
                             "player to fade. The cloak will still hide the player " +
                             "from enemies, but there will be no visible transparency.");
    }

    private void ApplyPlayerAlpha(float alpha)
    {
        for (int i = 0; i < _playerRenderers.Count; i++)
        {
            var sr = _playerRenderers[i];
            if (sr == null) continue;
            Color c = sr.color;
            // Fade relative to the renderer's own original alpha so a sprite
            // that was already partly transparent doesn't get brighter.
            c.a = _originalAlphas[i] * alpha;
            sr.color = c;
        }
    }

    private void RestorePlayerSprites()
    {
        for (int i = 0; i < _playerRenderers.Count; i++)
        {
            var sr = _playerRenderers[i];
            if (sr == null) continue;
            Color c = sr.color;
            c.a = _originalAlphas[i];
            sr.color = c;
        }
        _playerRenderers.Clear();
        _originalAlphas.Clear();
    }

    //  Shimmer VFX 

    private void BuildVFX()
    {
        DestroyVFX();

        _vfxRoot = new GameObject("CloakShimmerVFX");
        _vfxRoot.transform.SetParent(transform, worldPositionStays: false);
        _vfxRoot.transform.localPosition = Vector3.zero;

        Sprite ring = GetRingSprite();
        _shimmerRings = new SpriteRenderer[RING_COUNT];

        // Match the player's sorting layer so the shimmer sits with the body.
        string sortLayer = "Default";
        int baseOrder = 0;
        var bodySR = GetComponent<SpriteRenderer>();
        if (bodySR != null)
        {
            sortLayer = bodySR.sortingLayerName;
            baseOrder = bodySR.sortingOrder;
        }

        for (int i = 0; i < RING_COUNT; i++)
        {
            var go = new GameObject($"ShimmerRing{i}");
            go.transform.SetParent(_vfxRoot.transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ring;
            sr.sortingLayerName = sortLayer;
            // Render just behind the player body so it reads as a refraction
            // halo rather than covering the (already faint) player sprite.
            sr.sortingOrder = baseOrder - 1;
            sr.color = new Color(SHIMMER_COLOR.r, SHIMMER_COLOR.g, SHIMMER_COLOR.b, 0f);
            _shimmerRings[i] = sr;
        }

        _vfxTime = 0f;
    }

    private void UpdateVFX()
    {
        if (_shimmerRings == null) return;

        _vfxTime += Time.deltaTime;

        // Fade the whole effect in at the start and out near expiry so the
        // cloak's end is telegraphed (last ~1.2s pulses brighter/faster).
        float fadeIn = Mathf.Clamp01(_vfxTime / 0.35f);
        float endProximity = 1f - Mathf.Clamp01(_invisTimer / 1.2f); // 0 normally, →1 near end
        float master = fadeIn;

        for (int i = 0; i < _shimmerRings.Length; i++)
        {
            var sr = _shimmerRings[i];
            if (sr == null) continue;

            // Each ring breathes on its own staggered phase.
            float phase = _vfxTime * (1.6f + i * 0.5f) + i * Mathf.PI;
            float breathe = 0.5f + 0.5f * Mathf.Sin(phase);

            // Expiry warning: speed up + brighten.
            float warnBoost = 1f + endProximity * 2.5f;
            float warnPhase = Time.time * 9f * warnBoost;
            float warn = endProximity > 0f ? (0.5f + 0.5f * Mathf.Sin(warnPhase)) * endProximity : 0f;

            float scale = (0.9f + 0.35f * breathe) + warn * 0.25f;
            sr.transform.localScale = Vector3.one * scale;
            sr.transform.Rotate(0f, 0f, (28f + i * 14f) * Time.deltaTime);

            float alpha = (0.10f + 0.22f * breathe + warn * 0.30f) * master;
            sr.color = new Color(SHIMMER_COLOR.r, SHIMMER_COLOR.g, SHIMMER_COLOR.b, alpha);
        }
    }

    private void DestroyVFX()
    {
        if (_vfxRoot != null)
            Destroy(_vfxRoot);
        _vfxRoot = null;
        _shimmerRings = null;
    }

    /// <summary>Procedural soft ring sprite — no art asset needed.</summary>
    private static Sprite GetRingSprite()
    {
        if (_cachedRingSprite != null) return _cachedRingSprite;

        const int S = 96;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float outer = S * 0.46f;
        float inner = S * 0.30f;
        float mid = (outer + inner) * 0.5f;
        float halfBand = (outer - inner) * 0.5f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                // Soft band centred on `mid`, falling off to both edges.
                float t = 1f - Mathf.Clamp01(Mathf.Abs(d - mid) / halfBand);
                float a = t * t; // ease for a softer glow
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }

        tex.SetPixels(px);
        tex.Apply();
        _cachedRingSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _cachedRingSprite;
    }

    // ── Audio (best-effort; silent if events aren't wired) ──

    private void PlayCloakSound()
    {
        // Reuse an existing soft "whoosh"-style event if available. We guard
        // every access so a missing FMODEvents field never throws.
        TryPlayOneShot();
    }

    private void PlayUncloakSound()
    {
        TryPlayOneShot();
    }

    private void TryPlayOneShot()
    {
        if (AudioManager.instance == null || FMODEvents.instance == null) return;
        // TODO add sound for cloak activation
        var ev = FMODEvents.instance.dashSound;
        if (!ev.IsNull)
            AudioManager.instance.PlayOneShot(ev, transform.position);
    }
}
