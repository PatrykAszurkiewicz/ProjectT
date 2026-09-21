using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Self-contained procedural VFX for Boss3's expanded moveset
//   Boss3RiftMarker   — pre-telegraphs a teleport: a glitch tear + ghost at the spot
//                       the boss is ABOUT to appear, so the blink is readable.
//   Boss3RootSpike    — one thorn of the radial root-burst: a ground crack telegraph
//                       then a branch erupts upward and deals damage at its base.
//   Boss3CorruptTether— the thread from the boss's hand to a tower during a corruption
//                       channel, with corruption creeping into the tower as it charges.
//   Boss3Shockwave    — one-off ring for the phase-2 gear-shift.
//   Boss3HitSpark     — a crisp pip when the boss takes damage (hit feedback).

internal static class Boss3FXUtil
{
    public static SpriteRenderer MakeSprite(Transform parent, string name, Sprite sprite,
                                            Color col, int order, string layer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = col;
        sr.sortingOrder = order;
        if (!string.IsNullOrEmpty(layer)) sr.sortingLayerName = layer;
        return sr;
    }

    // One shared "Sprites/Default" material for every line this class builds.
    //
    // Was `new Material(Shader.Find("Sprites/Default"))` per LineRenderer, which is
    // a shader-registry string lookup and a fresh Material object EVERY time - and
    // this is a hot path (root-spike stalks and twigs, spawned per attack), so
    // it ran constantly rather than
    // once at setup. Nothing here sets per-instance material state; all colouring
    // goes through startColor/endColor, which are vertex colours. So one shared
    // instance covers every line.
    //
    // Assigned via sharedMaterial, never .material - reading the `.material` getter
    // makes Unity instantiate a per-renderer copy, which is the thing being avoided.
    private static Material _lineMat;

    // Play-mode exit destroys the material, but with domain reload disabled the
    // static field would still point at the dead object next Play. Same guard
    // Boss3Sprites uses.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLineMaterial() => _lineMat = null;

    private static Material LineMaterial
    {
        get
        {
            if (_lineMat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                _lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            return _lineMat;
        }
    }

    public static LineRenderer MakeLine(Transform parent, string name, int pts, float width,
                                        Color col, int order, string layer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.sharedMaterial = LineMaterial;
        lr.textureMode = LineTextureMode.Stretch;
        lr.numCapVertices = 3;
        lr.numCornerVertices = 3;
        lr.positionCount = pts;
        lr.startWidth = lr.endWidth = width;
        lr.startColor = lr.endColor = col;
        lr.sortingOrder = order;
        if (!string.IsNullOrEmpty(layer)) lr.sortingLayerName = layer;
        return lr;
    }
}

// RIFT MARKER — reads the incoming teleport.
public class Boss3RiftMarker : MonoBehaviour
{
    private const int ORDER = 2150;

    private SpriteRenderer _slit, _red, _cyan, _ghost;
    private float _life, _age;
    private bool _popped;

    public static Boss3RiftMarker Spawn(Vector3 pos, float life, string layer)
    {
        var go = new GameObject("Boss3_Rift");
        go.transform.position = pos;
        var m = go.AddComponent<Boss3RiftMarker>();
        m.Init(life, layer);
        return m;
    }

    private void Init(float life, string layer)
    {
        _life = Mathf.Max(0.05f, life);
        _ghost = Boss3FXUtil.MakeSprite(transform, "Ghost", Boss3Sprites.SoftDot,
                                        new Color(0.03f, 0.03f, 0.05f, 0f), ORDER - 2, layer);
        _red = Boss3FXUtil.MakeSprite(transform, "Red", Boss3Sprites.SoftDot,
                                      new Color(1f, 0.18f, 0.2f, 0.5f), ORDER - 1, layer);
        _cyan = Boss3FXUtil.MakeSprite(transform, "Cyan", Boss3Sprites.SoftDot,
                                       new Color(0.3f, 0.8f, 1f, 0.5f), ORDER - 1, layer);
        _slit = Boss3FXUtil.MakeSprite(transform, "Slit", Boss3Sprites.Spark,
                                       new Color(0.85f, 0.4f, 1f, 0.95f), ORDER, layer);
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float k = Mathf.Clamp01(_age / _life);
        float grow = Mathf.SmoothStep(0.25f, 1f, k);
        float flick = 0.7f + 0.3f * Mathf.Sin(Time.time * 40f);
        float t = Time.time;

        if (_slit != null)
        {
            _slit.transform.localScale = new Vector3(0.22f * flick, 2.4f * grow, 1f);
            var c = _slit.color; c.a = 0.95f * flick; _slit.color = c;
        }
        float split = 0.18f + 0.5f * k;
        AnimateHalf(_red, -split, grow, t);
        AnimateHalf(_cyan, split, grow, t);
        if (_ghost != null)
        {
            float gs = 1.4f + 1.4f * grow;
            _ghost.transform.localScale = new Vector3(gs * 0.7f, gs, 1f);
            var c = _ghost.color; c.a = 0.4f * k; _ghost.color = c;
        }

        // If nobody popped us (e.g. the boss died mid-blink), fade out and self-destruct.
        if (!_popped && _age > _life + 0.35f) Destroy(gameObject);
    }

    private void AnimateHalf(SpriteRenderer sr, float dx, float grow, float t)
    {
        if (sr == null) return;
        sr.transform.localPosition = new Vector3(dx, 0f, 0f);
        sr.transform.localScale = new Vector3(1.1f, 2.0f * grow, 1f);
        var c = sr.color; c.a = 0.45f + 0.2f * Mathf.Sin(t * 30f + dx); sr.color = c;
    }

    // Called as the boss materialises: a quick bright burst, then die.
    public void Pop()
    {
        if (_popped) return;
        _popped = true;
        StartCoroutine(PopRoutine());
    }

    private IEnumerator PopRoutine()
    {
        float t = 0f;
        const float dur = 0.14f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            if (_slit != null)
            {
                float s = Mathf.Lerp(2.4f, 0.1f, k);
                _slit.transform.localScale = new Vector3(0.5f * (1f - k) + 0.05f, s, 1f);
                var c = _slit.color; c.a = 1f - k; _slit.color = c;
            }
            FadeOut(_red, k); FadeOut(_cyan, k); FadeOut(_ghost, k);
            yield return null;
        }
        Destroy(gameObject);
    }

    private void FadeOut(SpriteRenderer sr, float k)
    {
        if (sr == null) return;
        var c = sr.color; c.a *= (1f - k); sr.color = c;
    }
}

// ROOT SPIKE — one thorn of the radial burst.
public class Boss3RootSpike : MonoBehaviour
{
    private enum P { Telegraph, Strike, Hold, Retract, Done }

    private Vector3 _base;
    private float _tele;
    private Boss3TreeHand.Settings _s;
    private string _layer;
    private System.Action<Vector2> _onImpact;

    private LineRenderer _stalk;
    private readonly List<LineRenderer> _twigs = new List<LineRenderer>();
    private SpriteRenderer _crack, _tipGlow;

    private P _p = P.Telegraph;
    private float _t;
    private bool _impact;
    private float _height;
    private float[] _wander;
    private float _wanderTimer;

    public static Boss3RootSpike Spawn(Vector3 basePos, float telegraph,
                                       Boss3TreeHand.Settings settings, string layer,
                                       System.Action<Vector2> onImpact)
    {
        var go = new GameObject("Boss3_RootSpike");
        go.transform.position = basePos;
        var s = go.AddComponent<Boss3RootSpike>();
        s.Init(basePos, telegraph, settings, layer, onImpact);
        return s;
    }

    private void Init(Vector3 basePos, float telegraph, Boss3TreeHand.Settings settings,
                      string layer, System.Action<Vector2> onImpact)
    {
        _base = basePos;
        _tele = Mathf.Max(0.05f, telegraph);
        _s = settings ?? new Boss3TreeHand.Settings();
        _layer = layer;
        _onImpact = onImpact;
        _height = Random.Range(2.3f, 3.2f);

        _wander = new float[8];
        RegenWander();

        _crack = Boss3FXUtil.MakeSprite(transform, "Crack", Boss3Sprites.SoftDot,
                                        _s.telegraphColor, _s.sortingOrder, layer);
        _crack.transform.position = _base;

        _stalk = Boss3FXUtil.MakeLine(transform, "Stalk", _wander.Length, _s.telegraphWidth,
                                      _s.barkColor, _s.sortingOrder, layer);
        for (int i = 0; i < 3; i++)
            _twigs.Add(Boss3FXUtil.MakeLine(transform, "Twig" + i, 3, _s.telegraphWidth * 0.6f,
                                            _s.barkColor, _s.sortingOrder, layer));

        _tipGlow = Boss3FXUtil.MakeSprite(transform, "Tip", Boss3Sprites.Spark,
                                          _s.strikeColor, _s.sortingOrder + 1, layer);
        _tipGlow.enabled = false;
    }

    private void RegenWander()
    {
        for (int i = 0; i < _wander.Length; i++)
        {
            float edge = Mathf.Sin(i / (float)(_wander.Length - 1) * Mathf.PI);
            _wander[i] = Random.Range(-1f, 1f) * _s.jitter * 0.6f * edge;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        _t += dt;
        _wanderTimer -= dt;
        if (_wanderTimer <= 0f) { _wanderTimer = Random.Range(0.05f, 0.12f); RegenWander(); }

        switch (_p)
        {
            case P.Telegraph:
                {
                    float k = Mathf.Clamp01(_t / _tele);
                    // Ground crack pulses; a tiny sprout hints upward so the eruption reads.
                    float pulse = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 9f));
                    if (_crack != null)
                    {
                        float s = Mathf.Lerp(0.6f, 1.5f, k) * (0.85f + 0.3f * pulse);
                        _crack.transform.localScale = new Vector3(s, s * 0.35f, 1f); // flat on the ground
                        Color c = _s.telegraphColor; c.a = 0.5f + 0.4f * pulse; _crack.color = c;
                    }
                    Render(0.12f * k, _s.telegraphWidth, _s.telegraphColor);
                    if (_t >= _tele) { _p = P.Strike; _t = 0f; }
                    break;
                }
            case P.Strike:
                {
                    float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, _s.strikeDuration + 0.03f));
                    float ext = Mathf.SmoothStep(0.12f, 1f, k);
                    float w = Mathf.Lerp(_s.telegraphWidth, _s.strikeWidth, k);
                    Render(ext, w, Color.Lerp(_s.telegraphColor, _s.strikeColor, k));
                    if (!_impact && k >= 1f)
                    {
                        _impact = true;
                        _onImpact?.Invoke(_base);
                        if (_tipGlow != null) _tipGlow.enabled = true;
                        _p = P.Hold; _t = 0f;
                    }
                    break;
                }
            case P.Hold:
                {
                    Render(1f, _s.strikeWidth, _s.strikeColor);
                    if (_t >= _s.holdDuration + 0.06f) { _p = P.Retract; _t = 0f; }
                    break;
                }
            case P.Retract:
                {
                    float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, _s.retractDuration));
                    float ext = 1f - k;
                    Color c = _s.strikeColor; c.a = 1f - k;
                    Render(ext, Mathf.Lerp(_s.strikeWidth, 0f, k), c);
                    if (_crack != null) { var cc = _crack.color; cc.a = (1f - k) * 0.5f; _crack.color = cc; }
                    if (k >= 1f) { _p = P.Done; Destroy(gameObject); }
                    break;
                }
        }

        // Tip glow fades after the strike.
        if (_tipGlow != null && _tipGlow.enabled)
        {
            var c = _tipGlow.color; c.a = Mathf.Max(0f, c.a - dt * 3f); _tipGlow.color = c;
        }
    }

    // Draw the stalk erupting straight UP from the base with horizontal wander.
    private void Render(float ext, float width, Color color)
    {
        if (_stalk == null) return;
        Vector3 up = Vector3.up;
        Vector3 perp = Vector3.right;
        float topH = _height * Mathf.Clamp01(ext);

        Color bark = _s.barkColor; bark.a = color.a;
        _stalk.startWidth = width;
        _stalk.endWidth = width * 0.3f;
        _stalk.startColor = bark;
        _stalk.endColor = color;

        int n = _wander.Length;
        Vector3 top = _base;
        for (int i = 0; i < n; i++)
        {
            float f = i / (float)(n - 1);
            Vector3 p = _base + up * (topH * f) + perp * _wander[i];
            _stalk.SetPosition(i, p);
            top = p;
        }

        for (int ti = 0; ti < _twigs.Count; ti++)
        {
            var tw = _twigs[ti];
            float baseF = 0.5f + 0.16f * ti;
            int wi = Mathf.Clamp((int)(baseF * (n - 1)), 0, n - 1);
            Vector3 bp = _base + up * (topH * baseF) + perp * _wander[wi];
            float side = (ti % 2 == 0) ? 1f : -1f;
            Vector3 dir = (up * 0.6f + perp * side).normalized;
            Vector3 tip = bp + dir * (topH * 0.22f);
            Color twTip = new Color(color.r, color.g, color.b, color.a * 0.9f);
            tw.startWidth = width * 0.55f; tw.endWidth = 0f;
            tw.startColor = bark; tw.endColor = twTip;
            tw.SetPosition(0, bp);
            tw.SetPosition(1, Vector3.Lerp(bp, tip, 0.5f) + perp * side * (_s.jitter * 0.2f));
            tw.SetPosition(2, tip);
        }

        if (_tipGlow != null)
        {
            _tipGlow.transform.position = top;
            float s = _s.strikeWidth * 4f;
            _tipGlow.transform.localScale = new Vector3(s, s, 1f);
        }
    }
}

// CORRUPT TETHER — boss hand → tower during a channel.
public class Boss3CorruptTether : MonoBehaviour
{
    private const int ORDER = 2120;

    private System.Func<Vector3> _from, _to;
    private LineRenderer _cord, _inner;
    private SpriteRenderer _towerGlow, _handGlow;
    private float[] _wander;
    private float _wanderTimer;
    private float _prog;
    private bool _ending;

    private static readonly Color CORRUPT_A = new Color(0.5f, 0.1f, 0.7f, 1f);  // violet
    private static readonly Color CORRUPT_B = new Color(1f, 0.2f, 0.15f, 1f);   // hot red

    public static Boss3CorruptTether Spawn(System.Func<Vector3> from, System.Func<Vector3> to, string layer)
    {
        var go = new GameObject("Boss3_CorruptTether");
        var m = go.AddComponent<Boss3CorruptTether>();
        m.Init(from, to, layer);
        return m;
    }

    private void Init(System.Func<Vector3> from, System.Func<Vector3> to, string layer)
    {
        _from = from; _to = to;
        _wander = new float[12];
        Regen();
        _cord = Boss3FXUtil.MakeLine(transform, "Cord", _wander.Length, 0.14f, CORRUPT_A, ORDER, layer);
        _inner = Boss3FXUtil.MakeLine(transform, "Inner", _wander.Length, 0.06f, Color.white, ORDER + 1, layer);
        _handGlow = Boss3FXUtil.MakeSprite(transform, "HandGlow", Boss3Sprites.SoftDot, CORRUPT_A, ORDER, layer);
        _towerGlow = Boss3FXUtil.MakeSprite(transform, "TowerGlow", Boss3Sprites.SoftDot, CORRUPT_A, ORDER + 2, layer);
    }

    private void Regen()
    {
        for (int i = 0; i < _wander.Length; i++)
        {
            float edge = Mathf.Sin(i / (float)(_wander.Length - 1) * Mathf.PI);
            _wander[i] = Random.Range(-1f, 1f) * 0.35f * edge;
        }
    }

    public void SetProgress(float p) => _prog = Mathf.Clamp01(p);

    public void Dissipate(bool interrupted)
    {
        _ending = true;
        StartCoroutine(EndRoutine(interrupted));
    }

    private void Update()
    {
        if (_ending) return;   // once dissipating, stop touching from()/to() (boss may be gone)
        if (_from == null || _to == null) return;
        float dt = Time.deltaTime;
        _wanderTimer -= dt;
        if (_wanderTimer <= 0f) { _wanderTimer = Random.Range(0.04f, 0.09f); Regen(); }

        Vector3 a = _from(); Vector3 b = _to();
        Vector3 dir = b - a; float len = dir.magnitude;
        Vector3 fwd = len > 1e-4f ? dir / len : Vector3.right;
        Vector3 perp = new Vector3(-fwd.y, fwd.x, 0f);

        Color hot = Color.Lerp(CORRUPT_A, CORRUPT_B, _prog);
        int n = _wander.Length;
        for (int i = 0; i < n; i++)
        {
            float f = i / (float)(n - 1);
            Vector3 p = Vector3.Lerp(a, b, f) + perp * (_wander[i] * (1f + 2f * f)); // wilder near the tower
            _cord.SetPosition(i, p);
            _inner.SetPosition(i, p);
        }
        _cord.startColor = CORRUPT_A; _cord.endColor = hot;
        float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 18f);
        Color ic = Color.Lerp(hot, Color.white, 0.5f); ic.a = pulse;
        _inner.startColor = _inner.endColor = ic;

        if (_handGlow != null)
        {
            _handGlow.transform.position = a;
            float s = 0.6f + 0.2f * pulse;
            _handGlow.transform.localScale = new Vector3(s, s, 1f);
            _handGlow.color = new Color(hot.r, hot.g, hot.b, 0.7f);
        }
        if (_towerGlow != null)
        {
            _towerGlow.transform.position = b;
            // Corruption swells into the tower as the channel charges.
            float s = Mathf.Lerp(0.8f, 3.2f, _prog) * (0.9f + 0.15f * pulse);
            _towerGlow.transform.localScale = new Vector3(s, s, 1f);
            _towerGlow.color = new Color(hot.r, hot.g, hot.b, Mathf.Lerp(0.25f, 0.8f, _prog));
        }
    }

    private IEnumerator EndRoutine(bool interrupted)
    {
        // Interrupt = a sharp snap; completion = a darker settle. Either way, fade fast.
        float dur = interrupted ? 0.18f : 0.3f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            SetAlpha(_cord, 1f - k);
            SetAlpha(_inner, 1f - k);
            if (_handGlow != null) { var c = _handGlow.color; c.a *= (1f - k); _handGlow.color = c; }
            if (_towerGlow != null)
            {
                // On interrupt the tower glow snaps outward (released); on completion it implodes.
                float s = interrupted ? Mathf.Lerp(3.2f, 5f, k) : Mathf.Lerp(3.2f, 0.2f, k);
                _towerGlow.transform.localScale = new Vector3(s, s, 1f);
                var c = _towerGlow.color; c.a *= (1f - k); _towerGlow.color = c;
            }
            yield return null;
        }
        Destroy(gameObject);
    }

    private void SetAlpha(LineRenderer lr, float a)
    {
        if (lr == null) return;
        Color s = lr.startColor; s.a = a; Color e = lr.endColor; e.a = a;
        lr.startColor = s; lr.endColor = e;
    }
}

// SHOCKWAVE — phase-2 gear-shift.
public class Boss3Shockwave : MonoBehaviour
{
    private const int ORDER = 2180;
    private SpriteRenderer _ring, _red, _cyan;
    private float _t;
    private const float LIFE = 0.55f;

    public static Boss3Shockwave Spawn(Vector3 pos, string layer)
    {
        var go = new GameObject("Boss3_Shockwave");
        go.transform.position = pos;
        go.AddComponent<Boss3Shockwave>().Init(layer);
        return go.GetComponent<Boss3Shockwave>();
    }

    private void Init(string layer)
    {
        _red = Boss3FXUtil.MakeSprite(transform, "Red", Boss3Sprites.SoftDot, new Color(1f, 0.2f, 0.2f, 0.8f), ORDER, layer);
        _cyan = Boss3FXUtil.MakeSprite(transform, "Cyan", Boss3Sprites.SoftDot, new Color(0.3f, 0.8f, 1f, 0.8f), ORDER, layer);
        _ring = Boss3FXUtil.MakeSprite(transform, "Ring", Boss3Sprites.Spark, new Color(1f, 0.6f, 0.4f, 0.95f), ORDER + 1, layer);
    }

    private void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / LIFE);
        float ease = 1f - (1f - k) * (1f - k);

        Grow(_ring, Mathf.Lerp(0.4f, 5f, ease), 1f - k, 0f);
        Grow(_red, Mathf.Lerp(0.6f, 6.5f, ease), (1f - k) * 0.8f, -ease * 0.5f);
        Grow(_cyan, Mathf.Lerp(0.6f, 6.5f, ease), (1f - k) * 0.8f, ease * 0.5f);

        if (k >= 1f) Destroy(gameObject);
    }

    private void Grow(SpriteRenderer sr, float scale, float alpha, float dx)
    {
        if (sr == null) return;
        sr.transform.localScale = new Vector3(scale, scale, 1f);
        sr.transform.localPosition = new Vector3(dx, 0f, 0f);
        var c = sr.color; c.a = Mathf.Clamp01(alpha); sr.color = c;
    }
}

// HIT SPARK — crisp feedback when the boss takes damage.
public class Boss3HitSpark : MonoBehaviour
{
    private SpriteRenderer _sr;
    private float _t, _life, _size;

    public static Boss3HitSpark Spawn(Vector3 pos, string layer, bool strong)
    {
        var go = new GameObject("Boss3_HitSpark");
        go.transform.position = pos + (Vector3)(Random.insideUnitCircle * 0.3f);
        var s = go.AddComponent<Boss3HitSpark>();
        s.Init(layer, strong);
        return s;
    }

    private void Init(string layer, bool strong)
    {
        _life = 0.18f;
        _size = strong ? 1.5f : 1.0f;
        Color c = strong ? new Color(1f, 0.95f, 0.8f, 1f) : new Color(0.85f, 0.9f, 1f, 0.9f);
        _sr = Boss3FXUtil.MakeSprite(transform, "Spark", Boss3Sprites.Spark, c, 2300, layer);
    }

    private void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _life);
        float pop = Mathf.Sin(k * Mathf.PI);           // quick swell then vanish
        if (_sr != null)
        {
            float s = _size * (0.4f + pop);
            _sr.transform.localScale = new Vector3(s, s, 1f);
            var c = _sr.color; c.a = 1f - k; _sr.color = c;
        }
        if (k >= 1f) Destroy(gameObject);
    }
}





