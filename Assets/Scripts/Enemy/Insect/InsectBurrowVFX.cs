using UnityEngine;
using System.Collections.Generic;

// InsectBurrowVFX
// Self-contained, procedural ground effects for the burrowing Insect.

[DisallowMultipleComponent]
public class InsectBurrowVFX : MonoBehaviour
{
    [Header("Sorting (auto-resolved from FogOverlay)")]
    [Tooltip("How many sorting-order steps BELOW the fog's lowest layer the ground " +
             "effects sit. Small, so they stay just under the fog but above everything else.")]
    [SerializeField] private int stepsBelowFog = 6;

    [Tooltip("Used only if no FogOverlay is present. Must stay above the grass Y-sort " +
             "band (sortOrderBase +/- spawnRadius*precision, ~1600) so effects aren't buried.")]
    [SerializeField] private int fallbackSortingOrder = 3000;

    [Header("Mound (the 'only the top is visible' cover)")]
    [Tooltip("Mound width as a multiple of the insect sprite width.")]
    [SerializeField] private float moundWidthFactor = 1.55f;
    [Tooltip("Height of the raised earth mound as a fraction of the sprite height " +
             "(the insect is hidden underground, so this is just the size of the " +
             "travelling dirt bump).")]
    [SerializeField] private float moundCoverHeightFactor = 0.5f;

    // Resolved sorting
    private string _sortLayer = "Default";
    private int _sortOrder = 3000;

    // Terrain Y-sort constants (from GrassCartoonOverlay). Used ONLY by the leap
    // shadow, which is a flat ground decal and must sort within the terrain band
    // so map obstacles draw on top of it — unlike the dust/crest/scars, which
    // intentionally stay above the terrain for tunnel-path visibility.
    private string _terrainLayer = "Default";
    private int _terrainBase = 1000;
    private float _terrainPrec = 10f;

    // Biome-derived colours
    private Color _dirt = new Color(0.36f, 0.26f, 0.17f, 1f);
    private Color _dust = new Color(0.60f, 0.50f, 0.38f, 1f);

    // Who / how big
    private Transform _follow;
    private float _spriteWidth = 1f;
    private float _spriteHeight = 1f;

    // The container everything parents to (world-space, so it doesn't inherit the
    // insect's squash/stretch scaling).
    private Transform _container;

    // Mound
    private SpriteRenderer _mound;
    private float _moundCover;        // current 0..1 raise amount
    private bool _moundActive;
    private bool _moundPinned;        // while leaping, the mound stays at a fixed hole
    private Vector3 _moundPinPos;

    // Leap shadow (a squashed dark blob on the ground under the airborne insect)
    private SpriteRenderer _shadow;
    private bool _shadowActive;
    private Vector3 _shadowGround;
    private float _shadowApex;        // 0 on the ground .. 1 at the top of the arc

    // Travelling soil crest — a raised, bobbing dirt bump that rides over the
    // insect while it tunnels. Sits ABOVE the grass (VFX sort band) so the tunnel
    // is clearly readable even though the underground body sprite is buried under
    // the terrain. Oriented along travel and gently animated.
    private SpriteRenderer _crest;
    private bool _crestActive;
    private Vector3 _crestPos;
    private Vector2 _crestDir = Vector2.right;
    private float _crestPhase;
    private float _crestAmp;           // eased 0..1 so it grows in / shrinks out

    // Live particles + scars (Update-driven, like EnemyDeathVFX)
    private readonly List<Particle> _particles = new List<Particle>(48);
    private readonly List<Scar> _scars = new List<Scar>(4);
    private readonly List<Ring> _rings = new List<Ring>(6);

    // Trail throttle
    private float _nextTrailTime;
    private float _nextRippleTime;
    private float _nextAirTime;

    //  Setup 

    public void Initialize(Transform follow, Bounds spriteWorldBounds)
    {
        _follow = follow;

        _spriteWidth = Mathf.Max(0.25f, spriteWorldBounds.size.x);
        _spriteHeight = Mathf.Max(0.25f, spriteWorldBounds.size.y);

        ResolveSortingBelowFog();
        ResolveBiomeColours();

        // World-space container so mound + particles are immune to the insect's
        // localScale squash/stretch during dive/pop.
        var go = new GameObject("[InsectBurrowVFX]");
        _container = go.transform;
        _container.position = _follow != null ? _follow.position : transform.position;

        BuildMound();
        BuildShadow();
        BuildCrest();
    }

    private void ResolveSortingBelowFog()
    {
        // FogOverlay is a component BiomeManager adds; grab whichever is live.
        var fog = FindAnyObjectByType<FogOverlay>();
        if (fog != null)
        {
            _sortLayer = string.IsNullOrEmpty(fog.sortingLayerName) ? "Default" : fog.sortingLayerName;
            // Fog's own lowest sublayer is (sortingOrder - 2); go a couple more below that.
            _sortOrder = fog.sortingOrder - 2 - Mathf.Max(1, stepsBelowFog);
        }
        else
        {
            _sortLayer = "Default";
            _sortOrder = fallbackSortingOrder;
        }

        // Resolve the terrain band (for the ground shadow) from the grass overlay
        // so the shadow interleaves with grass and obstacles exactly like the map.
        // Grass, layout obstacles and enemies all share the "Default" sorting
        // layer; the shadow must live there too so its order competes with them.
        var grass = FindAnyObjectByType<GrassCartoonOverlay>();
        if (grass != null)
        {
            _terrainBase = grass.sortOrderBase;
            _terrainPrec = grass.sortPrecision;
        }
    }

    private void ResolveBiomeColours()
    {
        BiomeType biome = BiomeType.Grass;
        var bm = FindAnyObjectByType<BiomeManager>();
        if (bm != null) biome = bm.activeBiome;

        switch (biome)
        {
            case BiomeType.Snow:
                _dirt = new Color(0.74f, 0.79f, 0.88f); break;      // packed snow / frost
            case BiomeType.Desert:
                _dirt = new Color(0.80f, 0.66f, 0.40f); break;      // sand
            case BiomeType.Wasteland:
                _dirt = new Color(0.40f, 0.28f, 0.42f); break;      // ashen purple (matches smog)
            case BiomeType.Stones:
                _dirt = new Color(0.42f, 0.46f, 0.38f); break;      // grey-green scree
            case BiomeType.Marsh:
                _dirt = new Color(0.33f, 0.30f, 0.20f); break;      // wet peat
            case BiomeType.Night:
                _dirt = new Color(0.20f, 0.19f, 0.24f); break;
            case BiomeType.PitchBlack:
            case BiomeType.Corruption:
                _dirt = new Color(0.13f, 0.14f, 0.12f); break;
            case BiomeType.Grass:
            case BiomeType.GrassCartoon:
            default:
                _dirt = new Color(0.37f, 0.27f, 0.17f); break;      // brown earth
        }
        _dirt.a = 1f;
        // A lighter kick-up tint for dust / rim so the effect pops off the ground.
        _dust = Color.Lerp(_dirt, Color.white, 0.38f);
        _dust.a = 1f;
    }

    //  Public playback API (called by InsectController) 

    // A burst of earth at a point — used for both the dive-in and the pop-out.
    public void Burst(Vector3 worldPos, float strength = 1f)
    {
        if (_container == null) return;

        int clods = Mathf.RoundToInt(Mathf.Lerp(9f, 16f, Mathf.Clamp01(strength)));
        for (int i = 0; i < clods; i++)
        {
            float ang = Random.Range(20f, 160f) * Mathf.Deg2Rad;      // mostly upward fan
            float spd = Random.Range(2.2f, 5.2f) * (0.7f + 0.6f * strength);
            Vector2 v = new Vector2(Mathf.Cos(ang) * Random.Range(-1f, 1f), Mathf.Sin(ang)) * spd;
            SpawnParticle(
                GetClodSprite(),
                worldPos + (Vector3)(Random.insideUnitCircle * 0.12f),
                v,
                startScale: Random.Range(0.10f, 0.22f) * _spriteHeight,
                life: Random.Range(0.45f, 0.8f),
                grav: -11f,
                spin: Random.Range(-540f, 540f),
                c0: _dirt * Random.Range(0.75f, 1.05f),
                c1: _dirt * 0.6f,
                soft: false);
        }

        int puffs = Mathf.RoundToInt(Mathf.Lerp(4f, 7f, Mathf.Clamp01(strength)));
        for (int i = 0; i < puffs; i++)
        {
            Vector2 v = new Vector2(Random.Range(-1.4f, 1.4f), Random.Range(0.4f, 2.0f));
            SpawnParticle(
                GetSoftDiscSprite(),
                worldPos + (Vector3)(Random.insideUnitCircle * 0.25f),
                v,
                startScale: Random.Range(0.5f, 0.9f) * _spriteWidth,
                life: Random.Range(0.4f, 0.7f),
                grav: 1.2f,                         // dust drifts up a touch
                spin: 0f,
                c0: new Color(_dust.r, _dust.g, _dust.b, 0.55f),
                c1: new Color(_dust.r, _dust.g, _dust.b, 0f),
                soft: true,
                growTo: 1.9f);
        }

        SpawnScar(worldPos, Random.Range(0.55f, 0.75f) * _spriteWidth);
    }

    // Extra kicked-up dust while the insect is digging in / churning underground.
    // Complements the mound-free sprite animations with a bit of airborne dirt so
    // the burrowing reads as physical. Throttled internally so it's cheap to call
    // every frame from InsectController.
    private float _nextDigDustTime;
    public void EmitDigDust(Vector3 worldPos)
    {
        if (_container == null || Time.time < _nextDigDustTime) return;
        _nextDigDustTime = Time.time + Random.Range(0.035f, 0.075f);

        // A low, soft dust puff rising off the disturbed ground.
        Vector2 puffVel = new Vector2(Random.Range(-1.1f, 1.1f), Random.Range(0.6f, 1.8f));
        SpawnParticle(
            GetSoftDiscSprite(),
            worldPos + (Vector3)(Random.insideUnitCircle * 0.18f),
            puffVel,
            startScale: Random.Range(0.40f, 0.70f) * _spriteWidth,
            life: Random.Range(0.30f, 0.55f),
            grav: 0.8f, spin: 0f,
            c0: new Color(_dust.r, _dust.g, _dust.b, 0.42f),
            c1: new Color(_dust.r, _dust.g, _dust.b, 0f),
            soft: true, growTo: 1.8f);

        // The odd small clod tossed up with it.
        if (Random.value < 0.5f)
            SpawnParticle(
                GetClodSprite(),
                worldPos + (Vector3)(Random.insideUnitCircle * 0.12f),
                new Vector2(Random.Range(-1.2f, 1.2f), Random.Range(1.4f, 2.8f)),
                startScale: Random.Range(0.07f, 0.13f) * _spriteHeight,
                life: Random.Range(0.30f, 0.50f),
                grav: -10f, spin: Random.Range(-360f, 360f),
                c0: _dirt, c1: _dirt * 0.6f, soft: false);
    }

    // Occasional crumbs + dust while moving underground. Throttled internally.
    public void EmitTrail(Vector3 worldPos, Vector2 travelDir)
    {
        if (_container == null || Time.time < _nextTrailTime) return;
        _nextTrailTime = Time.time + Random.Range(0.05f, 0.10f);

        Vector2 back = travelDir.sqrMagnitude > 0.001f ? -travelDir.normalized : Vector2.down;

        // A low dust puff kicked out behind the travel direction.
        SpawnParticle(
            GetSoftDiscSprite(),
            worldPos + (Vector3)(back * 0.2f) + (Vector3)(Random.insideUnitCircle * 0.15f),
            back * Random.Range(0.3f, 0.9f) + Vector2.up * Random.Range(0.2f, 0.7f),
            startScale: Random.Range(0.35f, 0.6f) * _spriteWidth,
            life: Random.Range(0.35f, 0.6f),
            grav: 0.6f, spin: 0f,
            c0: new Color(_dust.r, _dust.g, _dust.b, 0.4f),
            c1: new Color(_dust.r, _dust.g, _dust.b, 0f),
            soft: true, growTo: 1.7f);

        // The occasional small clod.
        if (Random.value < 0.6f)
        {
            SpawnParticle(
                GetClodSprite(),
                worldPos + (Vector3)(Random.insideUnitCircle * 0.15f),
                back * Random.Range(0.5f, 1.2f) + Vector2.up * Random.Range(1.2f, 2.6f),
                startScale: Random.Range(0.08f, 0.15f) * _spriteHeight,
                life: Random.Range(0.35f, 0.6f),
                grav: -10f, spin: Random.Range(-360f, 360f),
                c0: _dirt, c1: _dirt * 0.6f, soft: false);
        }

        // Leave a churned-earth mark along the tunnel line most steps, so a clear
        // (but temporary) disturbed-soil trace follows the insect underground.
        if (Random.value < 0.7f)
            SpawnScar(worldPos + (Vector3)(Random.insideUnitCircle * 0.1f),
                      Random.Range(0.4f, 0.65f) * _spriteWidth, startAlpha: 0.5f,
                      life: Random.Range(1.6f, 2.4f));
    }

    // Show/hide the covering mound and set how far it's raised (0 = flat/hidden,
    // 1 = fully covering the lower body). InsectController tweens `cover` on dive/pop.
    public void SetBurrowedVisual(bool active, float cover)
    {
        _moundPinned = false;                 // follow the insect (dig-in / tunnelling)
        _moundActive = active;
        _moundCover = Mathf.Clamp01(cover);
        if (_mound != null)
            _mound.enabled = active && _moundCover > 0.01f;
    }

    // Same, but the mound stays at a fixed world spot (the exit hole) instead of
    // following — used while the insect is airborne during its leap so the hole it
    // came out of sinks in place rather than flying up with it.
    public void SetBurrowedVisualPinned(bool active, float cover, Vector3 worldPos)
    {
        _moundPinned = true;
        _moundPinPos = worldPos;
        _moundActive = active;
        _moundCover = Mathf.Clamp01(cover);
        if (_mound != null)
            _mound.enabled = active && _moundCover > 0.01f;
    }

    // Position/scale the ground shadow under the airborne insect. apex01: 0 when
    // it's on the ground, ~1 at the top of the arc (shadow shrinks & fades up high).
    public void SetJumpShadow(Vector3 groundPos, float apex01)
    {
        _shadowActive = true;
        _shadowGround = groundPos;
        _shadowApex = Mathf.Clamp01(apex01);
    }

    public void HideJumpShadow()
    {
        _shadowActive = false;
        if (_shadow != null) _shadow.enabled = false;
    }

    //  Underground travel — visible surface cues 

    // Turn the travelling soil crest on/off. While on it rides over the insect
    // (positioned by SetTunnelCrest) so you can see the ground heaving as it
    // tunnels, even though the body sprite itself is buried under the terrain.
    public void ShowTunnelCrest(bool on)
    {
        _crestActive = on;
        if (!on && _crest != null && _crestAmp <= 0.01f)
            _crest.enabled = false;
    }

    // Update the crest's ground position + travel direction (call while tunnelling).
    public void SetTunnelCrest(Vector3 worldPos, Vector2 travelDir)
    {
        _crestPos = worldPos;
        if (travelDir.sqrMagnitude > 0.0001f)
            _crestDir = Vector2.Lerp(_crestDir, travelDir.normalized, 0.35f).normalized;
    }

    // A richer, rhythmic underground disturbance: a forward soil spray, an
    // expanding ground ripple ring, and rising dust — all above the grass so the
    // tunnel path is legible. Throttled internally; safe to call every frame.
    public void EmitUndergroundRipple(Vector3 worldPos, Vector2 travelDir)
    {
        if (_container == null || Time.time < _nextRippleTime) return;
        _nextRippleTime = Time.time + Random.Range(0.10f, 0.16f);

        Vector2 fwd = travelDir.sqrMagnitude > 0.001f ? travelDir.normalized : Vector2.right;

        // Expanding churned-earth ripple ring on the surface.
        SpawnRing(worldPos, maxRadius: Random.Range(0.55f, 0.85f) * _spriteWidth,
                  life: Random.Range(0.45f, 0.7f), startAlpha: 0.45f);

        // A little forward-thrown soil ahead of the crest (two angled clods).
        for (int s = -1; s <= 1; s += 2)
        {
            Vector2 side = new Vector2(-fwd.y, fwd.x) * s;
            Vector2 v = (fwd * Random.Range(0.8f, 1.6f) + side * Random.Range(0.4f, 0.9f)
                         + Vector2.up * Random.Range(1.4f, 2.4f));
            SpawnParticle(
                GetClodSprite(),
                worldPos + (Vector3)(fwd * 0.25f * _spriteWidth),
                v,
                startScale: Random.Range(0.09f, 0.15f) * _spriteHeight,
                life: Random.Range(0.30f, 0.5f),
                grav: -11f, spin: Random.Range(-420f, 420f),
                c0: _dirt, c1: _dirt * 0.6f, soft: false);
        }
    }

    //  Leap out / land 

    // The eruption when the insect breaks the surface to leap. A directional
    // geyser of soil biased along the leap direction, plus a fast shock ring.
    public void Erupt(Vector3 worldPos, Vector2 leapDir)
    {
        if (_container == null) return;

        Vector2 dir = leapDir.sqrMagnitude > 0.001f ? leapDir.normalized : Vector2.up;

        // Fast expanding shock ring at the exit hole.
        SpawnRing(worldPos, maxRadius: Random.Range(1.1f, 1.4f) * _spriteWidth,
                  life: 0.5f, startAlpha: 0.6f);

        // Upward soil geyser, fanned toward the leap direction.
        int clods = 16;
        for (int i = 0; i < clods; i++)
        {
            float up = Random.Range(3.2f, 6.5f);
            Vector2 lateral = new Vector2(Random.Range(-1.2f, 1.2f), 0f) + dir * Random.Range(0.6f, 2.2f);
            Vector2 v = new Vector2(lateral.x, up);
            SpawnParticle(
                GetClodSprite(),
                worldPos + (Vector3)(Random.insideUnitCircle * 0.12f),
                v,
                startScale: Random.Range(0.11f, 0.22f) * _spriteHeight,
                life: Random.Range(0.5f, 0.85f),
                grav: -13f, spin: Random.Range(-560f, 560f),
                c0: _dirt * Random.Range(0.8f, 1.05f), c1: _dirt * 0.55f, soft: false);
        }

        // Dust column.
        for (int i = 0; i < 6; i++)
        {
            SpawnParticle(
                GetSoftDiscSprite(),
                worldPos + (Vector3)(Random.insideUnitCircle * 0.2f),
                new Vector2(Random.Range(-1f, 1f), Random.Range(1.4f, 3.0f)),
                startScale: Random.Range(0.5f, 0.9f) * _spriteWidth,
                life: Random.Range(0.45f, 0.75f),
                grav: 1.4f, spin: 0f,
                c0: new Color(_dust.r, _dust.g, _dust.b, 0.5f),
                c1: new Color(_dust.r, _dust.g, _dust.b, 0f),
                soft: true, growTo: 2.0f);
        }

        SpawnScar(worldPos, Random.Range(0.6f, 0.85f) * _spriteWidth, startAlpha: 0.5f, life: 1.6f);
    }

    // Light dust motes trailing the airborne insect so the leap arc reads.
    // Throttled; call every frame while jumping.
    public void EmitAirTrail(Vector3 worldPos)
    {
        if (_container == null || Time.time < _nextAirTime) return;
        _nextAirTime = Time.time + Random.Range(0.02f, 0.05f);

        SpawnParticle(
            GetSoftDiscSprite(),
            worldPos + (Vector3)(Random.insideUnitCircle * 0.15f),
            new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.4f, 0.4f)),
            startScale: Random.Range(0.18f, 0.34f) * _spriteWidth,
            life: Random.Range(0.25f, 0.45f),
            grav: -1.2f, spin: 0f,
            c0: new Color(_dust.r, _dust.g, _dust.b, 0.32f),
            c1: new Color(_dust.r, _dust.g, _dust.b, 0f),
            soft: true, growTo: 1.6f);
    }

    // The dust slam when the leap lands. Radial ring of low clods + shock ring +
    // a fat dust puff, biased outward so it reads as an impact.
    public void LandingImpact(Vector3 worldPos, float strength = 1f)
    {
        if (_container == null) return;

        SpawnRing(worldPos, maxRadius: Mathf.Lerp(1.0f, 1.5f, Mathf.Clamp01(strength)) * _spriteWidth,
                  life: 0.45f, startAlpha: 0.6f);

        int clods = Mathf.RoundToInt(Mathf.Lerp(10f, 16f, Mathf.Clamp01(strength)));
        for (int i = 0; i < clods; i++)
        {
            float ang = Mathf.Deg2Rad * Random.Range(0f, 360f);
            Vector2 outDir = new Vector2(Mathf.Cos(ang), Mathf.Abs(Mathf.Sin(ang)) * 0.5f + 0.2f);
            Vector2 v = outDir * Random.Range(2.0f, 4.2f) * (0.7f + 0.5f * strength);
            SpawnParticle(
                GetClodSprite(),
                worldPos + (Vector3)(Random.insideUnitCircle * 0.14f),
                v,
                startScale: Random.Range(0.09f, 0.18f) * _spriteHeight,
                life: Random.Range(0.35f, 0.6f),
                grav: -12f, spin: Random.Range(-500f, 500f),
                c0: _dirt, c1: _dirt * 0.6f, soft: false);
        }

        for (int i = 0; i < 5; i++)
        {
            SpawnParticle(
                GetSoftDiscSprite(),
                worldPos + (Vector3)(Random.insideUnitCircle * 0.3f),
                new Vector2(Random.Range(-1.8f, 1.8f), Random.Range(0.3f, 1.2f)),
                startScale: Random.Range(0.6f, 1.0f) * _spriteWidth,
                life: Random.Range(0.4f, 0.65f),
                grav: 0.8f, spin: 0f,
                c0: new Color(_dust.r, _dust.g, _dust.b, 0.5f),
                c1: new Color(_dust.r, _dust.g, _dust.b, 0f),
                soft: true, growTo: 2.1f);
        }

        SpawnScar(worldPos, Random.Range(0.6f, 0.9f) * _spriteWidth, startAlpha: 0.5f, life: 1.8f);
    }

    // A broad, subtle brown soil poof that erupts from the ground. Used to MASK
    // the moment the buried-tail patch swaps from its pinned emergence pose to
    // following the attacking body (that swap otherwise reads as a small pop).
    // Everything here sorts in the terrain band, so map obstacles occlude it too.
    public void SoilBurst(Vector3 worldPos, float strength = 1f)
    {
        if (_container == null) return;
        float s = Mathf.Clamp(strength, 0.4f, 2f);

        // Low, wide dust dome — several big soft puffs spreading sideways so the
        // silhouette change underneath is hidden behind a curtain of soil dust.
        int puffs = Mathf.RoundToInt(6 * s);
        for (int i = 0; i < puffs; i++)
        {
            Vector2 dir = new Vector2(Random.Range(-1.9f, 1.9f), Random.Range(0.25f, 1.1f));
            Vector3 at = worldPos + (Vector3)(new Vector2(Random.Range(-0.45f, 0.45f), Random.Range(-0.1f, 0.15f)) * _spriteWidth);
            SpawnParticle(
                GetSoftDiscSprite(), at, dir * Random.Range(0.8f, 1.8f),
                startScale: Random.Range(0.7f, 1.15f) * _spriteWidth * s,
                life: Random.Range(0.45f, 0.75f),
                grav: 0.6f, spin: 0f,
                c0: new Color(_dust.r, _dust.g, _dust.b, 0.55f),
                c1: new Color(_dust.r, _dust.g, _dust.b, 0f),
                soft: true, growTo: 2.2f,
                explicitOrder: GroundOrder(at.y, 3));
        }

        // A scatter of solid clods thrown up through the dust for body.
        int clods = Mathf.RoundToInt(9 * s);
        for (int i = 0; i < clods; i++)
        {
            float ang = Mathf.Deg2Rad * Random.Range(0f, 360f);
            Vector2 outDir = new Vector2(Mathf.Cos(ang), Mathf.Abs(Mathf.Sin(ang)) * 0.6f + 0.35f);
            Vector3 at = worldPos + (Vector3)(Random.insideUnitCircle * 0.18f * _spriteWidth);
            SpawnParticle(
                GetClodSprite(), at, outDir * Random.Range(2.2f, 4.6f) * (0.7f + 0.5f * s),
                startScale: Random.Range(0.08f, 0.17f) * _spriteHeight,
                life: Random.Range(0.35f, 0.6f),
                grav: -12f, spin: Random.Range(-520f, 520f),
                c0: _dirt, c1: _dirt * 0.6f, soft: false,
                explicitOrder: GroundOrder(at.y, 2));
        }

        // Expanding ground ring + a fresh churn scar to seat it on the ground.
        SpawnRing(worldPos, maxRadius: Random.Range(0.9f, 1.3f) * _spriteWidth,
                  life: Random.Range(0.4f, 0.6f), startAlpha: 0.4f);
        SpawnScar(worldPos, Random.Range(0.7f, 1.0f) * _spriteWidth, startAlpha: 0.5f, life: 1.6f);
    }

    private void BuildMound()
    {
        var go = new GameObject("BurrowMound");
        go.transform.SetParent(_container, false);
        _mound = go.AddComponent<SpriteRenderer>();
        _mound.sprite = GetMoundSprite();
        _mound.sortingLayerName = _sortLayer;
        _mound.sortingOrder = _sortOrder + 1;   // mound one step over scars/dust so it reads as solid ground
        _mound.color = _dirt;
        _mound.enabled = false;
    }

    private void BuildShadow()
    {
        var go = new GameObject("BurrowJumpShadow");
        go.transform.SetParent(_container, false);
        _shadow = go.AddComponent<SpriteRenderer>();
        _shadow.sprite = GetSoftDiscSprite();
        _shadow.sortingLayerName = _terrainLayer;   // "Default" — same layer as obstacles/grass
        // Order is set every frame in UpdateShadow from the shadow's ground Y so
        // map obstacles occlude it correctly (see UpdateShadow).
        _shadow.color = new Color(0.03f, 0.03f, 0.05f, 0.45f);
        _shadow.enabled = false;
    }

    private void BuildCrest()
    {
        var go = new GameObject("BurrowTunnelCrest");
        go.transform.SetParent(_container, false);
        _crest = go.AddComponent<SpriteRenderer>();
        _crest.sprite = GetMoundSprite();
        _crest.sortingLayerName = _terrainLayer;   // "Default" — order set each frame in UpdateCrest
        _crest.color = _dirt;
        _crest.enabled = false;
    }

    // Position, scale, orient and gently animate the travelling soil crest.
    private void UpdateCrest()
    {
        if (_crest == null) return;

        // Ease the crest in when active and out when not, so it grows/sinks
        // instead of popping.
        float target = _crestActive ? 1f : 0f;
        _crestAmp = Mathf.MoveTowards(_crestAmp, target, Time.deltaTime * 4.5f);

        if (_crestAmp <= 0.01f)
        {
            _crest.enabled = false;
            return;
        }

        _crestPhase += Time.deltaTime * 9f;
        float bob = Mathf.Sin(_crestPhase) * 0.06f * _spriteHeight;   // small heave

        float w = _spriteWidth * 1.35f * _crestAmp;
        float h = _spriteHeight * 0.42f * _crestAmp;
        _crest.transform.localScale = new Vector3(w / MOUND_ASPECT, h, 1f);

        // Point the ridge along travel: rotate the (upright, bottom-pivot) mound so
        // its length lies across the direction of motion, and flip for left travel.
        float angle = Mathf.Atan2(_crestDir.y, _crestDir.x) * Mathf.Rad2Deg;
        bool left = _crestDir.x < 0f;
        _crest.flipX = left;

        // Anchor the base a touch below the insect's ground point, plus the heave.
        Vector3 p = _crestPos;
        float crestY = p.y - _spriteHeight * 0.30f + bob;
        _crest.transform.position = new Vector3(p.x, crestY, p.z);
        // Sort the raised ridge as a ground decal so a map obstacle the insect
        // tunnels under draws on top of it (+2 keeps it over its own scar/ripple).
        _crest.sortingOrder = GroundOrder(crestY, 2);
        // Lean the ridge slightly toward travel for a sense of motion.
        _crest.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Clamp((left ? angle + 180f : angle) * 0.15f, -12f, 12f));

        var c = _dirt;
        c.a = Mathf.SmoothStep(0f, 1f, _crestAmp);
        _crest.color = c;
        _crest.enabled = true;
    }

    private void UpdateShadow()
    {
        if (_shadow == null) return;
        if (!_shadowActive) { _shadow.enabled = false; return; }

        // Sit just under the feet line; shrink and fade as the insect rises.
        float baseW = _spriteWidth * 0.9f;
        float scale = baseW * (1f - 0.45f * _shadowApex);
        _shadow.transform.localScale = new Vector3(scale, scale * 0.42f, 1f); // squashed ellipse
        float shadowY = _shadowGround.y - _spriteHeight * 0.42f;
        _shadow.transform.position = new Vector3(_shadowGround.x, shadowY, _shadowGround.z);

        // Sort the shadow as a ground decal in the terrain band, so an obstacle
        // whose base is in front of the shadow draws on top of it. The -1 keeps
        // it just under a co-located insect/obstacle so it reads as a cast shadow.
        _shadow.sortingOrder = _terrainBase + Mathf.RoundToInt(-shadowY * _terrainPrec) - 1;

        var c = _shadow.color;
        c.a = 0.45f * (1f - 0.6f * _shadowApex);
        _shadow.color = c;
        _shadow.enabled = true;
    }

    private void LateUpdate()
    {
        UpdateShadow();
        UpdateCrest();

        if (_mound == null || _follow == null) return;

        if (_moundActive && _moundCover > 0.01f)
        {
            // Anchor: follow the insect while digging/tunnelling, or hold at the
            // fixed exit hole while it's airborne mid-leap.
            Vector3 anchor = _moundPinned ? _moundPinPos : _follow.position;
            // The mound's top ridge should sit around the sprite's vertical middle
            // when fully raised, hiding the lower body. We scale a unit-tall sprite
            // to the target world size and drop it as `cover` falls to 0.
            float coverH = _spriteHeight * moundCoverHeightFactor;
            float w = _spriteWidth * moundWidthFactor;

            // The mound sprite is 1 world-unit TALL at scale 1 (PPU == texH), but
            // MOUND_ASPECT units WIDE, so the X scale is corrected by that aspect
            // to hit the requested world width exactly.
            _mound.transform.localScale = new Vector3(w / MOUND_ASPECT, coverH, 1f);

            // Sprite pivot is bottom-centre, so its base sits on `pos.y + baseOffset`.
            // We anchor the base slightly below the insect's feet and let the dome
            // rise up over the body. As cover->0 the whole pile sinks out of view.
            Vector3 p = anchor;
            float baseY = p.y - _spriteHeight * 0.45f;
            float sink = (1f - _moundCover) * coverH;   // slide down as it recedes
            _mound.transform.position = new Vector3(p.x, baseY - sink, p.z);

            var c = _dirt; c.a = Mathf.SmoothStep(0f, 1f, _moundCover);
            _mound.color = c;
            _mound.enabled = true;
        }
        else
        {
            _mound.enabled = false;
        }
    }

    //  Particle + scar simulation 

    private void Update()
    {
        float dt = Time.deltaTime;

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.age += dt;
            float t = p.age / p.life;
            if (t >= 1f || p.go == null)
            {
                if (p.go != null) Destroy(p.go);
                _particles.RemoveAt(i);
                continue;
            }

            p.vel.y += p.grav * dt;
            p.vel *= 0.94f;
            p.go.transform.position += (Vector3)p.vel * dt;
            if (p.spin != 0f) p.go.transform.Rotate(0f, 0f, p.spin * dt);

            float s = p.soft
                ? Mathf.Lerp(1f, p.growTo, t)          // dust expands
                : Mathf.Lerp(1f, 0f, t * t);            // clods shrink away
            p.go.transform.localScale = p.baseScale * Mathf.Max(0.001f, s);

            p.sr.color = Color.Lerp(p.c0, p.c1, p.soft ? t : t * t);
        }

        for (int i = _scars.Count - 1; i >= 0; i--)
        {
            var s = _scars[i];
            s.age += dt;
            float t = s.age / s.life;
            if (t >= 1f || s.go == null)
            {
                if (s.go != null) Destroy(s.go);
                _scars.RemoveAt(i);
                continue;
            }
            // Quick settle, then a slow fade so a disturbed-earth mark lingers.
            float grow = Mathf.SmoothStep(0.6f, 1f, Mathf.Clamp01(t * 3f));
            s.go.transform.localScale = Vector3.one * s.radius * 2f * grow;
            var c = s.color;
            c.a = s.startAlpha * (1f - Mathf.SmoothStep(0.35f, 1f, t));
            s.sr.color = c;
        }

        // Expanding shock/ripple rings: grow outward fast, fade as they go.
        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            var rg = _rings[i];
            rg.age += dt;
            float rt = rg.age / rg.life;
            if (rt >= 1f || rg.go == null)
            {
                if (rg.go != null) Destroy(rg.go);
                _rings.RemoveAt(i);
                continue;
            }
            float grow = Mathf.SmoothStep(0f, 1f, rt);
            float radius = Mathf.Lerp(0.15f, rg.maxRadius, grow);
            rg.go.transform.localScale = new Vector3(radius * 2f, radius * 1.1f, 1f); // slightly squashed to the ground plane
            var rc = rg.color;
            rc.a = rg.startAlpha * (1f - rt) * (1f - rt);
            rg.sr.color = rc;
        }
    }

    // Sort a flat ground decal (scar / ripple ring / crest / soil burst) into the
    // terrain band on the SAME bottom-edge convention map obstacles use (y - 0.5),
    // so an obstacle in front of the decal draws on top of it — exactly like grass.
    // bias orders co-located decals against each other (e.g. scar below ring).
    private const float DecalGroundYOffset = -0.5f;
    private int GroundOrder(float worldY, int bias)
    {
        return _terrainBase + Mathf.RoundToInt(-(worldY + DecalGroundYOffset) * _terrainPrec) + bias;
    }

    private void SpawnParticle(Sprite sprite, Vector3 pos, Vector2 vel, float startScale,
                               float life, float grav, float spin, Color c0, Color c1,
                               bool soft, float growTo = 1f, int explicitOrder = int.MinValue)
    {
        var go = new GameObject("burrowP");
        go.transform.SetParent(_container, true);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * startScale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        // Ground bursts pass an explicit terrain-band order so obstacles occlude
        // them; airborne dust keeps the default (just under the mound).
        sr.sortingLayerName = explicitOrder == int.MinValue ? _sortLayer : _terrainLayer;
        sr.sortingOrder = explicitOrder == int.MinValue ? _sortOrder : explicitOrder;
        sr.color = c0;

        _particles.Add(new Particle
        {
            go = go,
            sr = sr,
            vel = vel,
            baseScale = Vector3.one * startScale,
            life = Mathf.Max(0.05f, life),
            grav = grav,
            spin = spin,
            c0 = c0,
            c1 = c1,
            soft = soft,
            growTo = growTo
        });
    }

    private void SpawnScar(Vector3 pos, float radius, float startAlpha = 0.5f, float life = 1.5f)
    {
        var go = new GameObject("burrowScar");
        go.transform.SetParent(_container, true);
        go.transform.position = pos;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSoftDiscSprite();
        sr.sortingLayerName = _terrainLayer;        // "Default" — same layer as obstacles
        sr.sortingOrder = GroundOrder(pos.y, -2);   // ground decal, under ripples; obstacles occlude it
        Color c = Color.Lerp(_dirt, Color.black, 0.35f); c.a = startAlpha;
        sr.color = c;

        _scars.Add(new Scar
        {
            go = go,
            sr = sr,
            radius = Mathf.Max(0.05f, radius),
            color = c,
            startAlpha = startAlpha,
            life = Mathf.Max(0.2f, life)
        });
    }

    private void SpawnRing(Vector3 pos, float maxRadius, float life, float startAlpha = 0.5f)
    {
        var go = new GameObject("burrowRing");
        go.transform.SetParent(_container, true);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.3f;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetRingSprite();
        sr.sortingLayerName = _terrainLayer;        // "Default" — same layer as obstacles
        sr.sortingOrder = GroundOrder(pos.y, -1);   // ground ripple; obstacles in front occlude it
        Color c = Color.Lerp(_dust, _dirt, 0.4f); c.a = startAlpha;
        sr.color = c;

        _rings.Add(new Ring
        {
            go = go,
            sr = sr,
            maxRadius = Mathf.Max(0.1f, maxRadius),
            color = c,
            startAlpha = startAlpha,
            life = Mathf.Max(0.15f, life)
        });
    }

    private void OnDestroy()
    {
        if (_container != null) Destroy(_container.gameObject);
    }

    //  Data 

    private class Particle
    {
        public GameObject go; public SpriteRenderer sr;
        public Vector2 vel; public Vector3 baseScale;
        public float age, life, grav, spin, growTo;
        public Color c0, c1; public bool soft;
    }

    private class Scar
    {
        public GameObject go; public SpriteRenderer sr;
        public float age, life, radius, startAlpha; public Color color;
    }

    private class Ring
    {
        public GameObject go; public SpriteRenderer sr;
        public float age, life, maxRadius, startAlpha; public Color color;
    }

    //  Procedural sprites (generated once, shared) 

    private static Sprite _soft, _clod, _moundSprite, _ring;

    // A soft hollow ring (bright annulus, transparent centre + edges) used for
    // expanding shock / ripple rings. White so SpriteRenderer.color tints it.
    private static Sprite GetRingSprite()
    {
        if (_ring != null) return _ring;
        const int R = 64;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[R * R];
        Vector2 c = new Vector2((R - 1) * 0.5f, (R - 1) * 0.5f);
        const float mid = 0.72f;    // ring radius as a fraction of half-size
        const float thick = 0.22f;  // ring thickness
        for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / (R * 0.5f); // 0..1
                float a = 1f - Mathf.Clamp01(Mathf.Abs(d - mid) / thick);
                a *= a;
                px[y * R + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        _ring = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), R);
        return _ring;
    }

    // Mound texture is 128x72 with PPU == 72, so at scale 1 it is 1 unit tall and
    // (128/72) units wide. Used to correct the mound's X scale to a target width.
    private const float MOUND_ASPECT = 128f / 72f;

    private static Sprite GetSoftDiscSprite()
    {
        if (_soft != null) return _soft;
        const int R = 64;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[R * R];
        Vector2 c = new Vector2((R - 1) * 0.5f, (R - 1) * 0.5f);
        for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / (R * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                a = a * a;                         // soft falloff
                px[y * R + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        _soft = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), R);
        return _soft;
    }

    private static Sprite GetClodSprite()
    {
        if (_clod != null) return _clod;
        const int R = 16;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color[R * R];
        // An irregular blob so tumbling clods don't look like squares.
        Vector2[] poly = { new(8, 15), new(13, 11), new(15, 6), new(11, 1), new(4, 2), new(1, 7), new(3, 12) };
        for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
                px[y * R + x] = InPoly(x, y, poly) ? Color.white : Color.clear;
        tex.SetPixels(px); tex.Apply();
        _clod = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), R);
        return _clod;
    }

    // A dome-shaped dirt pile: opaque, widest at the base, rounded ridge on top,
    // with vertical shading + a bright rim baked into RGB (multiplied by the
    // biome dirt tint). Pivot is bottom-centre so it sits ON the ground line.
    private static Sprite GetMoundSprite()
    {
        if (_moundSprite != null) return _moundSprite;
        const int W = 128, H = 72;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[W * H];
        float cx = (W - 1) * 0.5f;
        float rx = W * 0.5f;

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float nx = (x - cx) / rx;                 // -1..1 across width
                float domeTop = (nx * nx <= 1f) ? H * Mathf.Sqrt(1f - nx * nx) : 0f;

                float a, g;
                if (y <= domeTop && Mathf.Abs(nx) < 1f)
                {
                    // Soft horizontal edge + soft top ridge.
                    float edgeH = 1f - Mathf.SmoothStep(0.82f, 1f, Mathf.Abs(nx));
                    float distTop = (domeTop - y);
                    float edgeT = Mathf.SmoothStep(0f, 3.5f, distTop);
                    a = Mathf.Clamp01(edgeH * edgeT);

                    // Shading: dark base -> lighter mid -> bright ridge highlight.
                    float vy = (domeTop > 0.001f) ? y / domeTop : 0f;   // 0 base .. 1 ridge
                    g = Mathf.Lerp(0.55f, 0.92f, vy);
                    g += Mathf.SmoothStep(0.72f, 1f, vy) * 0.18f;        // rim light near the top
                    g += (nx < 0f ? 0.05f : -0.03f);                    // faint side light
                    g = Mathf.Clamp01(g);
                }
                else { a = 0f; g = 0f; }

                px[y * W + x] = new Color(g, g, g, a);
            }
        }

        // A few darker speckles for a bit of clumped-earth texture.
        var rng = new System.Random(1234);
        for (int i = 0; i < 120; i++)
        {
            int x = rng.Next(6, W - 6);
            int y = rng.Next(2, H - 6);
            int idx = y * W + x;
            if (px[idx].a > 0.5f)
            {
                float d = 0.72f;
                px[idx] = new Color(px[idx].r * d, px[idx].g * d, px[idx].b * d, px[idx].a);
            }
        }

        tex.SetPixels(px); tex.Apply();
        // PPU = H so the sprite is exactly 1 world-unit tall at scale 1 (we scale
        // it up to the desired world size in LateUpdate). Pivot bottom-centre.
        _moundSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), H);
        return _moundSprite;
    }

    private static bool InPoly(int x, int y, Vector2[] poly)
    {
        int n = poly.Length, c = 0;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % n];
            if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y))
            {
                float t = (y - a.y) / (b.y - a.y);
                if (x < a.x + t * (b.x - a.x)) c++;
            }
        }
        return (c & 1) == 1;
    }
}

