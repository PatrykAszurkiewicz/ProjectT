using System.Collections.Generic;
using UnityEngine;

// Small green "+" signs that float up around a player while they stand inside a
// heal tower's range.
//
// Zero setup: nothing needs to be added to the Player prefab. Tower.HealOnePlayer
// calls HealPlusEmitter.Ping(stats, ...) every heal tick for each player in range;
// the first ping adds this component, later pings just extend the "active" window.
// When pings stop (player walked out, tower destroyed / out of energy) the emitter
// stops spawning and the pluses already in the air finish fading out.
//
// Fully procedural (sprite generated at runtime, small pool, no allocations after
// warm-up), matching the rest of the project's runtime-built FX.
[DisallowMultipleComponent]
public class HealPlusEmitter : MonoBehaviour
{
    [Header("Emission")]
    [Tooltip("Pluses spawned per second while the player is in heal range.")]
    public float emitRate = 5f;
    [Tooltip("Keep emitting when the player is already at full HP (i.e. just standing in range).")]
    public bool emitAtFullHealth = true;
    [Tooltip("Pluses spawn in a ring around the player, this far from the centre (world units).")]
    public float spawnRadius = 0.45f;
    [Tooltip("Spawn centre relative to the player pivot (world units). Raise Y if the pivot is at the feet.")]
    public Vector2 centerOffset = new Vector2(0f, 0.35f);

    [Header("Motion")]
    public float lifetime = 0.9f;
    [Tooltip("Upward speed (world units / second).")]
    public float riseSpeed = 0.8f;
    [Tooltip("Side-to-side sway amplitude (world units).")]
    public float swayAmplitude = 0.06f;

    [Header("Look")]
    [Tooltip("Size of one plus (world units).")]
    public float plusSize = 0.2f;
    public Color color = new Color(0.35f, 1f, 0.45f, 1f);
    [Tooltip("Sorting order above the player's sprite.")]
    public int sortingOffset = 5;

    private struct Plus
    {
        public Transform tr;
        public SpriteRenderer sr;
        public Vector2 offset;  // relative to player, so pluses follow the player
        public float age;
        public float life;
        public float swayPhase;
        public float sizeMul;
    }

    private readonly List<Plus> _live = new List<Plus>();
    private readonly Stack<Plus> _pool = new Stack<Plus>();
    private float _activeUntil = -1f;
    private float _spawnAccum;
    private CharacterStats _stats;
    private SpriteRenderer _playerSr;

    private static Sprite _plusSprite;

    /// <summary>
    /// Called by a heal tower each heal tick for a player inside its range.
    /// <paramref name="holdSeconds"/> should exceed the tower's heal interval so the
    /// effect doesn't flicker between ticks.
    /// </summary>
    public static void Ping(Component player, float holdSeconds)
    {
        if (player == null) return;
        var e = player.GetComponent<HealPlusEmitter>();
        if (e == null) e = player.gameObject.AddComponent<HealPlusEmitter>();
        e._activeUntil = Mathf.Max(e._activeUntil, Time.time + holdSeconds);
    }

    private void Awake()
    {
        _stats = GetComponent<CharacterStats>();
        _playerSr = GetComponentInChildren<SpriteRenderer>();
    }

    private void Update()
    {
        bool emitting = Time.time < _activeUntil
                        && (_stats == null || !_stats.IsDead())
                        && (emitAtFullHealth || _stats == null || _stats.currentHealth < _stats.maxHealth);

        if (emitting)
        {
            _spawnAccum += emitRate * Time.deltaTime;
            while (_spawnAccum >= 1f)
            {
                _spawnAccum -= 1f;
                Spawn();
            }
        }
        else
        {
            _spawnAccum = 0f;
        }

        Animate(Time.deltaTime);
    }

    private void Spawn()
    {
        Plus p = _pool.Count > 0 ? _pool.Pop() : CreatePlus();

        // Random point in a ring, biased toward the lower half so pluses rise
        // past the body instead of spawning above the head.
        float ang = Random.Range(0f, Mathf.PI * 2f);
        float r = spawnRadius * Random.Range(0.5f, 1f);
        p.offset = centerOffset + new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r * 0.6f - 0.1f);
        p.age = 0f;
        p.life = lifetime * Random.Range(0.8f, 1.2f);
        p.swayPhase = Random.Range(0f, Mathf.PI * 2f);
        p.sizeMul = Random.Range(0.75f, 1.15f);
        p.tr.gameObject.SetActive(true);
        _live.Add(p);
    }

    private void Animate(float dt)
    {
        if (_live.Count == 0) return;

        Vector3 origin = transform.position;
        int layer = _playerSr != null ? _playerSr.sortingLayerID : 0;
        int order = (_playerSr != null ? _playerSr.sortingOrder : 0) + sortingOffset;

        // The pluses are children of the player (auto-cleanup), so undo the player's
        // scale to keep plusSize in world units.
        Vector3 ls = transform.lossyScale;
        float sx = Mathf.Abs(ls.x) > 1e-4f ? 1f / Mathf.Abs(ls.x) : 1f;
        float sy = Mathf.Abs(ls.y) > 1e-4f ? 1f / Mathf.Abs(ls.y) : 1f;

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Plus p = _live[i];
            p.age += dt;
            if (p.age >= p.life)
            {
                p.tr.gameObject.SetActive(false);
                _pool.Push(p);
                _live.RemoveAt(i);
                continue;
            }

            float t = p.age / p.life;
            p.offset.y += riseSpeed * dt;
            float sway = Mathf.Sin(p.swayPhase + p.age * 6f) * swayAmplitude;

            // Pop in (first 15%), hold, fade out (last 50%).
            float alpha = t < 0.15f ? t / 0.15f : (t > 0.5f ? 1f - (t - 0.5f) / 0.5f : 1f);
            float pop = t < 0.15f ? Mathf.Lerp(0.5f, 1.1f, t / 0.15f) : Mathf.Lerp(1.1f, 0.8f, (t - 0.15f) / 0.85f);
            float size = plusSize * p.sizeMul * pop;

            p.tr.position = origin + new Vector3(p.offset.x + sway, p.offset.y, 0f);
            p.tr.localScale = new Vector3(size * sx, size * sy, 1f);

            Color c = color;
            c.a *= alpha;
            p.sr.color = c;
            p.sr.sortingLayerID = layer;
            p.sr.sortingOrder = order;

            _live[i] = p;
        }
    }

    private Plus CreatePlus()
    {
        var go = new GameObject("HealPlus");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = PlusSprite();
        go.SetActive(false);
        return new Plus { tr = go.transform, sr = sr };
    }

    private void OnDisable()
    {
        // Hide anything mid-flight (e.g. player downed / disabled) and stop emitting.
        for (int i = 0; i < _live.Count; i++)
        {
            if (_live[i].tr != null) _live[i].tr.gameObject.SetActive(false);
            _pool.Push(_live[i]);
        }
        _live.Clear();
        _activeUntil = -1f;
        _spawnAccum = 0f;
    }

    // 16x16 white plus with a soft 1px edge, pivot centred, 1 world unit wide at
    // scale 1 (the per-plus scale then sets the real size). Tinted via sr.color.
    private static Sprite PlusSprite()
    {
        if (_plusSprite != null) return _plusSprite;

        const int N = 16;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float half = (N - 1) * 0.5f;
        const float arm = 2.6f;   // half-thickness of each bar (px)
        const float len = 7.2f;   // half-length of each bar (px)
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float dx = Mathf.Abs(x - half), dy = Mathf.Abs(y - half);
                // Signed-distance-ish coverage for the two bars, 1px soft edge.
                float h = Mathf.Min(arm - dy, len - dx);
                float v = Mathf.Min(arm - dx, len - dy);
                float a = Mathf.Clamp01(Mathf.Max(h, v) + 0.5f);
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();

        _plusSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        return _plusSprite;
    }
}
