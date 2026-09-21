using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// GENERATOR ENERGY NETWORK
//   ZERO DECAY FOR ITSELF
//   LINKS TO NEIGHBOURING TOWERS. Every tower inside the generator's
//      generationRange gets a GeneratorLinkReceiver, which
//        cancels that tower's natural energy decay while the link is live, and
//        trickles energy (= health) back into it, reviving depleted towers.
//   FEEDS THE CENTRAL CORE. If the core is inside coreLinkRange the generator
//      pipes energy into it too.
//   VISUALS. Procedural flowing conduits

[DisallowMultipleComponent]
public class GeneratorEnergyNetwork : MonoBehaviour
{
    #region Singleton / bootstrap

    private static GeneratorEnergyNetwork _instance;
    private static bool _quitting;

    // Domain reload can be disabled in the editor, so statics have to be cleared
    // between Play sessions exactly like Tower.ResetStatics does.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        _quitting = false;
        ReleaseSharedAssets();
    }

    public static GeneratorEnergyNetwork Instance => _quitting ? null : _instance;

    /// Called by Tower.Start() on the first generator. Safe to call repeatedly.
    /// Deliberately NOT called from OnDestroy paths — spawning a manager into a
    /// closing scene is the bug EnergyManager.Instance documents.
    public static GeneratorEnergyNetwork EnsureExists()
    {
        if (_quitting) return null;
        if (_instance != null) return _instance;

        _instance = FindFirstObjectByType<GeneratorEnergyNetwork>();
        if (_instance != null) return _instance;

        // Prefer living on the EnergyManager so the tuning shows up next to the
        // rest of the energy settings in the inspector.
        var em = EnergyManager.Instance;
        GameObject host = em != null ? em.gameObject : new GameObject("GeneratorEnergyNetwork");
        _instance = host.AddComponent<GeneratorEnergyNetwork>();
        return _instance;
    }

    #endregion

    #region Tuning — linking

    [Header("Linking")]
    [Tooltip("Master switch. OFF removes every link, receiver component and visual " +
             "and returns the board to vanilla behaviour.")]
    public bool enableNetwork = true;

    [Tooltip("Link radius in world units. 0 = use each generator's own " +
             "generationRange (4 on the stock GeneratorTower prefab).")]
    [Min(0f)]
    public float linkRangeOverride = 0f;

    [Tooltip("Added to whichever link radius is in use. Handy for nudging reach " +
             "without changing the generator's targeting range.")]
    public float linkRangeBonus = 0f;

    [Tooltip("Most towers one generator can power at once. Nearest win. 0 = uncapped.")]
    [Min(0)]
    public int maxLinksPerGenerator = 6;

    [Tooltip("Let generators link to each other. They already have zero decay, so " +
             "this only matters for the repair trickle and the visuals.")]
    public bool linkOtherGenerators = true;

    [Tooltip("How often the link map is rebuilt (seconds). Visuals still update " +
             "every frame; this only re-evaluates WHO is connected to WHOM.")]
    [Min(0.05f)]
    public float refreshInterval = 0.4f;

    #endregion

    #region Tuning — energy effects

    [Header("Linked towers")]
    [Tooltip("Cancel the natural energy decay of every linked tower while the link " +
             "is live. This is the 'compensated by the connection' half.")]
    public bool cancelDecayOnLinked = true;

    [Tooltip("Energy (= tower health) restored per second, per incoming link.")]
    [Min(0f)]
    public float regenPerSecondPerLink = 1.5f;

    [Tooltip("Ceiling on the total regen a single tower can receive, however many " +
             "generators reach it. 0 = uncapped.")]
    [Min(0f)]
    public float maxRegenPerSecond = 4f;

    [Tooltip("Keep trickling into a tower that has already hit 0 energy, bringing " +
             "it back online. OFF = links only maintain towers that are still up.")]
    public bool reviveDepletedTowers = true;

    [Header("Central core")]
    [Tooltip("Feed the Central Core when it sits inside coreLinkRange.")]
    public bool supplyCore = true;

    [Tooltip("Core link radius in world units. 0 = the tower link radius × " +
             "coreRangeMultiplier, so the core is reachable from a bit further out.")]
    [Min(0f)]
    public float coreLinkRangeOverride = 0f;

    // Was 1.5: the core linked from 1.5x the generator's range, i.e. well outside
    // the drawn ring, which read as a bug. 1 = the core connects exactly when its
    // CENTRE is inside the ring. Raise it again if you want the big core to be
    // reachable from further out (the ring then shows only the tower radius).
    [Tooltip("Core link radius = tower link radius x this (when no override is set).\n" +
             "1 = the core must be inside the same ring as the towers. The distance is " +
             "measured to the core's CENTRE, not its edge.")]
    [Min(0.1f)]
    public float coreRangeMultiplier = 1f;

    [Tooltip("Energy per second delivered to the core, per connected generator.")]
    [Min(0f)]
    public float coreEnergyPerSecondPerGenerator = 2f;

    [Tooltip("Ceiling on the combined core feed. 0 = uncapped.")]
    [Min(0f)]
    public float maxCoreEnergyPerSecond = 6f;

    [Header("Generator")]
    [Tooltip("Also zero the generator's OWN generation self-consumption " +
             "(Tower.generatorSelfConsumption), so a generator with zero decay " +
             "truly never loses energy. The original value is cached and restored " +
             "if you switch this off.")]
    public bool generatorZeroSelfConsumption = true;

    #endregion

    #region Tuning — visuals

    [Header("Link visuals")]
    public bool showLinks = true;

    public Color towerLinkColor = new Color(0.36f, 0.86f, 1f, 0.85f);
    public Color coreLinkColor = new Color(0.55f, 1f, 0.72f, 0.9f);

    [Min(0.01f)] public float linkWidth = 0.11f;
    [Tooltip("Width multiplier for the additive halo drawn under the conduit.")]
    [Min(1f)] public float glowWidthScale = 3.2f;
    public bool showGlow = true;

    [Tooltip("World units the energy pulse travels per second. Negative reverses it.")]
    public float flowSpeed = 2.2f;

    [Tooltip("World length of one repeat of the pulse texture.")]
    [Min(0.1f)] public float flowTileLength = 1.25f;

    [Tooltip("How far the conduit bows out sideways at its midpoint (world units).")]
    public float sagAmount = 0.22f;

    [Tooltip("Amplitude of the lazy sine wobble layered on top of the bow.")]
    public float wobbleAmount = 0.07f;
    public float wobbleSpeed = 1.3f;

    [Range(4, 64)] public int linkSegments = 24;

    [Tooltip("Fade a link in/out over this many seconds instead of popping it.")]
    [Min(0f)] public float linkFadeTime = 0.25f;

    [Header("Light packets")]
    public bool showPackets = true;
    [Range(0, 6)] public int packetsPerLink = 3;
    [Min(0.01f)] public float packetSize = 0.22f;

    [Header("Receiver aura (linked towers)")]
    public bool showReceiverAura = true;
    public Color receiverAuraColor = new Color(0.42f, 0.9f, 1f, 0.55f);
    [Tooltip("Aura diameter as a multiple of the tower sprite's largest world side.")]
    [Min(0.1f)] public float receiverAuraScale = 1.55f;
    [Tooltip("Degrees per second the dashed ring rotates.")]
    public float receiverAuraSpin = 34f;
    [Min(0f)] public float receiverAuraPulseSpeed = 2.2f;
    [Tooltip("Draw the ring BEHIND the tower sprite. Off = in front.")]
    public bool receiverAuraBehindTower = true;

    [Tooltip("Local offset for the ring, in the tower's own units. Nudge Y down to " +
             "sit the ring on the tower's base rather than its centre.")]
    public Vector2 receiverAuraOffset = Vector2.zero;

    [Header("Y-sort (mirrors EnergyManager's supply-beam convention)")]
    [Tooltip("Must match GrassCartoonOverlay.sortPrecision / EnergyManager.beamSortPrecision.")]
    public float sortPrecision = 10f;
    [Tooltip("Must match GrassCartoonOverlay.sortOrderBase / EnergyManager.beamSortOrderBase.")]
    public int sortOrderBase = 1000;
    public float sortYOffset = -0.3f;
    [Tooltip("Bias above the y-sorted foreground order. Kept below the supply " +
             "beam's bias (5) so a player supply beam still draws on top.")]
    public int sortBias = 2;

    #endregion

    #region Runtime state

    private struct Candidate
    {
        public Tower tower;
        public float distSqr;
    }

    private struct ActiveLink
    {
        public Tower generator;
        public Tower targetTower;   // null for a core link
        public CentralCore targetCore;
        public LinkVisual visual;
    }

    private readonly List<ActiveLink> _links = new List<ActiveLink>();
    private readonly List<GeneratorLinkReceiver> _receivers = new List<GeneratorLinkReceiver>();
    private readonly List<GeneratorSelfSustain> _selfSustains = new List<GeneratorSelfSustain>();
    private readonly List<Candidate> _candidates = new List<Candidate>();
    private readonly List<LinkVisual> _visualPool = new List<LinkVisual>();

    private static readonly System.Comparison<Candidate> NearestFirst =
        (a, b) => a.distSqr.CompareTo(b.distSqr);

    private CentralCore _core;
    private Transform _visualRoot;
    private float _refreshTimer;
    private int _passId;

    // Core feed accumulator — flushed on a tick instead of every frame so the
    // core's OnEnergyChanged listeners (health bar, visual state) are not woken
    // 60 times a second for a fractional change.
    private const float FlushInterval = 0.1f;
    private float _coreAccum, _coreFlushTimer;

    #endregion

    #region Unity lifecycle

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            // A scene-authored instance plus the bootstrapped one. Keep the first.
            Destroy(this);
            return;
        }
        _instance = this;
    }

    void OnEnable()
    {
        if (_instance == null) _instance = this;
        _refreshTimer = 0f;   // rebuild immediately
    }

    void OnDisable()
    {
        TearDownEverything();
    }

    void OnDestroy()
    {
        TearDownEverything();
        if (_visualRoot != null) Destroy(_visualRoot.gameObject);
        if (_instance == this)
        {
            _instance = null;
            ReleaseSharedAssets();
        }
    }

    void OnApplicationQuit() => _quitting = true;

    void Update()
    {
        if (_quitting) return;

        if (!enableNetwork)
        {
            if (_links.Count > 0 || _receivers.Count > 0 || _selfSustains.Count > 0)
                TearDownEverything();
            return;
        }

        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer <= 0f)
        {
            _refreshTimer = Mathf.Max(0.05f, refreshInterval);
            RebuildLinks();
        }

        TickCoreFeed(Time.deltaTime);
        TickVisuals(Time.deltaTime);
    }

    #endregion

    #region Link map

    [ContextMenu("Rebuild Links Now")]
    public void RebuildLinks()
    {
        var towers = Tower.ActiveTowers;
        _passId++;

        ReleaseAllVisuals();
        _links.Clear();

        if (towers == null || towers.Count == 0)
        {
            CommitReceivers();
            PruneSelfSustains();
            return;
        }

        CentralCore core = ResolveCore();
        bool coreUsable = supplyCore && core != null && !core.IsDestroyed();

        for (int g = 0; g < towers.Count; g++)
        {
            Tower gen = towers[g];
            if (!IsLiveGenerator(gen)) continue;

            ApplySelfSustain(gen);

            float range = LinkRangeFor(gen);
            if (range <= 0f) continue;
            float rangeSqr = range * range;
            Vector2 genPos = gen.transform.position;

            // ── neighbouring towers ────────────────────────────────────────────
            _candidates.Clear();
            for (int t = 0; t < towers.Count; t++)
            {
                if (t == g) continue;
                Tower other = towers[t];
                // NOTE: a depleted / damage-disabled tower is deliberately still a
                // valid target — that is the whole point of the repair trickle.
                // Only genuinely gone objects are skipped.
                if (other == null || !other.gameObject.activeInHierarchy) continue;
                if (!linkOtherGenerators && other.IsGenerator()) continue;

                float d = (((Vector2)other.transform.position) - genPos).sqrMagnitude;
                if (d > rangeSqr) continue;

                _candidates.Add(new Candidate { tower = other, distSqr = d });
            }

            int take = _candidates.Count;
            if (maxLinksPerGenerator > 0 && take > maxLinksPerGenerator)
            {
                _candidates.Sort(NearestFirst);
                take = maxLinksPerGenerator;
            }

            for (int i = 0; i < take; i++)
            {
                Tower target = _candidates[i].tower;
                GetOrAddReceiver(target).Accumulate(_passId, regenPerSecondPerLink);

                _links.Add(new ActiveLink
                {
                    generator = gen,
                    targetTower = target,
                    targetCore = null,
                    visual = AcquireVisual(gen, target.gameObject, false)
                });
            }

            // ── central core 
            if (coreUsable)
            {
                float coreRange = CoreLinkRangeFor(gen);
                float coreDistSqr = (((Vector2)core.GetPosition()) - genPos).sqrMagnitude;
                if (coreDistSqr <= coreRange * coreRange)
                {
                    _links.Add(new ActiveLink
                    {
                        generator = gen,
                        targetTower = null,
                        targetCore = core,
                        visual = AcquireVisual(gen, core.gameObject, true)
                    });
                }
            }
        }

        CommitReceivers();
        PruneSelfSustains();
    }

    private bool IsLiveGenerator(Tower t)
    {
        // IsOperational() == not depleted, not disabled by damage, not destroyed.
        // A generator that is down stops powering the grid, which is the readable
        // behaviour: knock out the generator and its cluster starts bleeding again.
        return t != null && t.gameObject.activeInHierarchy && t.IsGenerator() && t.IsOperational();
    }

    // FIX (tether range buff didn't add links): this read the raw generationRange, so
    // the tether's FAR-zone buff grew the range ring but never the link radius. It now
    // applies the tower's tether multiplier on top of override/bonus.
    //
    // Public so TowerRangeIndicator draws EXACTLY this radius. Before, the ring showed
    // generationRange and ignored linkRangeOverride / linkRangeBonus, so the two could
    // disagree even without a tether.
    public float LinkRangeFor(Tower gen, bool includeTether = true)
    {
        if (gen == null) return 0f;
        float baseRange = linkRangeOverride > 0f ? linkRangeOverride : gen.generationRange;
        float r = Mathf.Max(0f, baseRange + linkRangeBonus);
        return includeTether ? r * gen.GenerationRangeMultiplier : r;
    }

    public float CoreLinkRangeFor(Tower gen)
    {
        if (coreLinkRangeOverride > 0f) return coreLinkRangeOverride;
        return LinkRangeFor(gen) * Mathf.Max(0.1f, coreRangeMultiplier);
    }

    private CentralCore ResolveCore()
    {
        // Re-resolved whenever it goes null: TowerDefenseMap destroys and rebuilds
        // the core between stages, so caching it forever would leave a dead ref.
        if (_core == null) _core = FindFirstObjectByType<CentralCore>();
        return _core;
    }

    private GeneratorLinkReceiver GetOrAddReceiver(Tower tower)
    {
        var rec = tower.GetComponent<GeneratorLinkReceiver>();
        if (rec == null)
        {
            rec = tower.gameObject.AddComponent<GeneratorLinkReceiver>();
            _receivers.Add(rec);
        }
        else if (!_receivers.Contains(rec))
        {
            _receivers.Add(rec);
        }
        return rec;
    }

    private void CommitReceivers()
    {
        for (int i = _receivers.Count - 1; i >= 0; i--)
        {
            var rec = _receivers[i];
            if (rec == null) { _receivers.RemoveAt(i); continue; }

            rec.Commit(_passId, maxRegenPerSecond, cancelDecayOnLinked);

            if (rec.LinkCount <= 0)
            {
                _receivers.RemoveAt(i);
                Destroy(rec);   // OnDestroy tears its aura down
            }
        }
    }

    #endregion

    #region Generator self-sustain

    private void ApplySelfSustain(Tower gen)
    {
        var tag = gen.GetComponent<GeneratorSelfSustain>();

        if (!generatorZeroSelfConsumption)
        {
            if (tag != null)
            {
                tag.Restore();
                _selfSustains.Remove(tag);
                Destroy(tag);
            }
            return;
        }

        if (tag == null)
        {
            tag = gen.gameObject.AddComponent<GeneratorSelfSustain>();
            _selfSustains.Add(tag);
        }
        else if (!_selfSustains.Contains(tag))
        {
            _selfSustains.Add(tag);
        }
        tag.Apply();
    }

    private void PruneSelfSustains()
    {
        for (int i = _selfSustains.Count - 1; i >= 0; i--)
        {
            var tag = _selfSustains[i];
            bool keep = tag != null
                        && tag.gameObject.activeInHierarchy
                        && generatorZeroSelfConsumption;
            if (keep) continue;

            // Restore() puts the prefab's original generatorSelfConsumption back,
            // so flipping the option off mid-run is fully reversible.
            if (tag != null) { tag.Restore(); Destroy(tag); }
            _selfSustains.RemoveAt(i);
        }
    }

    #endregion

    #region Core feed

    private void TickCoreFeed(float dt)
    {
        if (!supplyCore || coreEnergyPerSecondPerGenerator <= 0f) { _coreAccum = 0f; return; }

        CentralCore core = ResolveCore();
        if (core == null || core.IsDestroyed()) { _coreAccum = 0f; return; }

        int feeders = 0;
        for (int i = 0; i < _links.Count; i++)
            if (_links[i].targetCore != null && IsLiveGenerator(_links[i].generator)) feeders++;

        if (feeders == 0) { _coreAccum = 0f; _coreFlushTimer = 0f; return; }

        float rate = coreEnergyPerSecondPerGenerator * feeders;
        if (maxCoreEnergyPerSecond > 0f) rate = Mathf.Min(rate, maxCoreEnergyPerSecond);

        if (core.GetEnergy() >= core.GetMaxEnergy()) { _coreAccum = 0f; return; }

        _coreAccum += rate * dt;
        _coreFlushTimer += dt;
        if (_coreFlushTimer < FlushInterval) return;

        _coreFlushTimer = 0f;
        if (_coreAccum > 0f) core.SupplyEnergy(_coreAccum);
        _coreAccum = 0f;
    }

    #endregion

    #region Visuals

    private Transform VisualRoot
    {
        get
        {
            if (_visualRoot == null)
            {
                // Deliberately left at the scene root rather than parented to this
                // manager: the manager usually rides on the EnergyManager GameObject,
                // and inheriting any non-unit scale from it would silently resize
                // every light packet. LineRenderers are world-space so they would not
                // care, but the packet sprites would. Destroyed explicitly in
                // OnDestroy, and it lives in the gameplay scene so a scene change
                // takes it with everything else.
                var go = new GameObject("GeneratorLinkVisuals");
                _visualRoot = go.transform;
            }
            return _visualRoot;
        }
    }

    private LinkVisual AcquireVisual(Tower from, GameObject to, bool isCoreLink)
    {
        if (!showLinks) return null;

        // Stable identity for this generator→target pair.
        int key = (from.GetInstanceID() * 73856093) ^ (to.GetInstanceID() * 19349663);

        // Prefer the visual that was driving this exact pair last pass. Reusing it
        // keeps the scroll phase, wobble and fade continuous — grabbing whatever
        // happened to be free would make every conduit jitter twice a second.
        LinkVisual free = null;
        for (int i = 0; i < _visualPool.Count; i++)
        {
            var candidate = _visualPool[i];
            if (candidate.inUse) continue;
            if (candidate.Key == key) { candidate.Bind(key, isCoreLink); return candidate; }
            if (free == null) free = candidate;
        }

        if (free == null)
        {
            free = new LinkVisual(this, VisualRoot);
            _visualPool.Add(free);
        }

        free.Bind(key, isCoreLink);
        return free;
    }

    private void ReleaseAllVisuals()
    {
        for (int i = 0; i < _visualPool.Count; i++) _visualPool[i].Release();
    }

    private void TickVisuals(float dt)
    {
        // Retire pooled visuals that were not re-acquired this pass.
        for (int i = 0; i < _visualPool.Count; i++)
            if (!_visualPool[i].inUse) _visualPool[i].FadeOut(dt);

        if (!showLinks) return;

        for (int i = 0; i < _links.Count; i++)
        {
            var link = _links[i];
            if (link.visual == null) continue;

            Tower gen = link.generator;
            if (gen == null) { link.visual.Hide(); continue; }

            Vector3 a = gen.transform.position;
            Vector3 b;
            bool live = IsLiveGenerator(gen);

            if (link.targetCore != null)
            {
                if (link.targetCore.IsDestroyed()) { link.visual.Hide(); continue; }
                b = link.targetCore.GetPosition();
            }
            else
            {
                if (link.targetTower == null) { link.visual.Hide(); continue; }
                b = link.targetTower.transform.position;
            }

            link.visual.Tick(dt, a, b, live);
        }
    }

    #endregion

    #region Teardown

    private void TearDownEverything()
    {
        _links.Clear();

        for (int i = 0; i < _visualPool.Count; i++) _visualPool[i].Destroy();
        _visualPool.Clear();

        for (int i = 0; i < _receivers.Count; i++)
            if (_receivers[i] != null) Destroy(_receivers[i]);
        _receivers.Clear();

        for (int i = 0; i < _selfSustains.Count; i++)
        {
            if (_selfSustains[i] == null) continue;
            _selfSustains[i].Restore();
            Destroy(_selfSustains[i]);
        }
        _selfSustains.Clear();

        _coreAccum = 0f;
        _coreFlushTimer = 0f;
    }

    #endregion

    #region Y-sort helper

    /// Same convention as EnergyManager's supply beam and GrassCartoonOverlay:
    /// anchor on the foreground (lower-Y) endpoint, then nudge forward.
    public int SortOrderFor(Vector3 a, Vector3 b)
    {
        int oa = sortOrderBase + Mathf.RoundToInt(-(a.y + sortYOffset) * sortPrecision);
        int ob = sortOrderBase + Mathf.RoundToInt(-(b.y + sortYOffset) * sortPrecision);
        return Mathf.Max(oa, ob) + sortBias;
    }

    #endregion

    #region Shared procedural assets

    // Built once, shared by every link and aura, released when the manager dies.
    private static Texture2D _flowTex, _dotTex, _ringTex;
    private static Sprite _dotSprite, _ringSprite;
    private static Shader _spriteShader;

    private static Shader SpriteShader =>
        _spriteShader != null ? _spriteShader : (_spriteShader = Shader.Find("Sprites/Default"));

    public static Texture2D FlowTexture
    {
        get
        {
            if (_flowTex != null) return _flowTex;

            const int W = 128, H = 4;
            _flowTex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "GenLinkFlow"
            };
            var px = new Color[W * H];
            for (int x = 0; x < W; x++)
            {
                float u = (float)x / W;
                // One travelling pulse per tile, sitting on a dim constant conduit.
                float d = Mathf.Abs(u - 0.5f) * 2f;              // 0 centre → 1 edges
                float pulse = Mathf.Pow(1f - d, 7f);
                float spark = Mathf.Pow(1f - d, 40f) * 0.6f;      // tight hot core
                float a = Mathf.Clamp01(0.30f + pulse * 0.75f + spark);
                for (int y = 0; y < H; y++) px[y * W + x] = new Color(1f, 1f, 1f, a);
            }
            _flowTex.SetPixels(px);
            _flowTex.Apply();
            return _flowTex;
        }
    }

    public static Sprite DotSprite
    {
        get
        {
            if (_dotSprite != null) return _dotSprite;

            const int S = 64;
            _dotTex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "GenLinkDot"
            };
            var px = new Color[S * S];
            Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
            float r = S * 0.5f;
            for (int x = 0; x < S; x++)
                for (int y = 0; y < S; y++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c) / r;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * a;                       // soft glow falloff
                    a += Mathf.Pow(Mathf.Clamp01(1f - d * 2.6f), 3f) * 0.8f;   // hot centre
                    px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            _dotTex.SetPixels(px);
            _dotTex.Apply();

            // PPU == texture size, so the sprite is exactly 1×1 world units at scale 1.
            _dotSprite = Sprite.Create(_dotTex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
            _dotSprite.name = "GenLinkDot";
            return _dotSprite;
        }
    }

    /// Dashed ring used as the "powered" marker on linked towers. Matches the
    /// hand-rolled ring sprites already used by Tower's generation aura and
    /// CoreRepairSystems' regen aura, but broken into segments so the spin reads.
    public static Sprite RingSprite
    {
        get
        {
            if (_ringSprite != null) return _ringSprite;

            const int S = 160;
            const int Segments = 12;
            const float Duty = 0.62f;      // fraction of each segment that is lit
            _ringTex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "GenLinkRing"
            };
            var px = new Color[S * S];
            Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
            float outer = S * 0.48f;
            float inner = S * 0.37f;
            float mid = (outer + inner) * 0.5f;
            float half = (outer - inner) * 0.5f;

            for (int x = 0; x < S; x++)
                for (int y = 0; y < S; y++)
                {
                    Vector2 p = new Vector2(x, y) - c;
                    float dist = p.magnitude;
                    if (dist > outer || dist < inner) { px[y * S + x] = Color.clear; continue; }

                    // Smooth radial band.
                    float radial = Mathf.Clamp01(1f - Mathf.Abs(dist - mid) / half);
                    radial = Mathf.SmoothStep(0f, 1f, radial);

                    // Angular dashes.
                    float ang = Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f) + 0.5f;   // 0..1
                    float seg = Mathf.Repeat(ang * Segments, 1f);
                    float dash = Mathf.SmoothStep(0f, 0.18f, seg) *
                                 (1f - Mathf.SmoothStep(Duty - 0.18f, Duty, seg));

                    px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radial * dash));
                }
            _ringTex.SetPixels(px);
            _ringTex.Apply();

            _ringSprite = Sprite.Create(_ringTex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
            _ringSprite.name = "GenLinkRing";
            return _ringSprite;
        }
    }

    public static Material CreateLineMaterial(bool additive)
    {
        var mat = new Material(SpriteShader);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", additive
            ? (int)UnityEngine.Rendering.BlendMode.One
            : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        return mat;
    }

    private static void ReleaseSharedAssets()
    {
        SafeDestroy(_dotSprite); _dotSprite = null;
        SafeDestroy(_ringSprite); _ringSprite = null;
        SafeDestroy(_flowTex); _flowTex = null;
        SafeDestroy(_dotTex); _dotTex = null;
        SafeDestroy(_ringTex); _ringTex = null;
        _spriteShader = null;
    }

    internal static void SafeDestroy(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }

    #endregion

    #region LinkVisual

    /// One procedural conduit: a textured line that scrolls, an additive halo and
    /// a few travelling light packets. Pooled — never rebuilt per refresh.
    private class LinkVisual
    {
        public bool inUse;

        private readonly GeneratorEnergyNetwork _net;
        private readonly GameObject _root;
        private readonly LineRenderer _beam, _glow;
        private readonly Material _beamMat, _glowMat;
        private readonly List<SpriteRenderer> _packets = new List<SpriteRenderer>();

        private Vector3[] _points = new Vector3[2];
        private float _phase, _side, _scroll, _alpha;
        private bool _isCoreLink;

        // Gradients and their key arrays are reused. Building fresh ones every
        // frame per link is a steady GC drip for no benefit — SetKeys copies the
        // values, and assigning colorGradient copies again.
        private readonly Gradient _gradBeam = new Gradient();
        private readonly Gradient _gradGlow = new Gradient();
        private readonly GradientColorKey[] _beamColorKeys = new GradientColorKey[2];
        private readonly GradientAlphaKey[] _beamAlphaKeys = new GradientAlphaKey[4];
        private readonly GradientColorKey[] _glowColorKeys = new GradientColorKey[2];
        private readonly GradientAlphaKey[] _glowAlphaKeys = new GradientAlphaKey[3];

        public LinkVisual(GeneratorEnergyNetwork net, Transform parent)
        {
            _net = net;

            _root = new GameObject("GeneratorLink");
            _root.transform.SetParent(parent, false);

            _beamMat = CreateLineMaterial(false);
            _beamMat.mainTexture = FlowTexture;

            _glowMat = CreateLineMaterial(true);

            _beam = MakeLine("Conduit", _beamMat);
            _glow = MakeLine("Halo", _glowMat);

            SetActive(false);
        }

        private LineRenderer MakeLine(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.useWorldSpace = true;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.receiveShadows = false;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            lr.positionCount = 2;
            return lr;
        }

        public int Key { get; private set; }

        public void Bind(int key, bool isCoreLink)
        {
            inUse = true;
            _isCoreLink = isCoreLink;

            if (Key != key)
            {
                Key = key;
                // Deterministic per-pair phase / bow direction, so the same two
                // towers always get the same curve and a rebuild changes nothing.
                _phase = Mathf.Abs(key % 1000) / 1000f * Mathf.PI * 2f;
                _side = ((key >> 3) & 1) == 0 ? 1f : -1f;
                _scroll = 0f;
                _alpha = 0f;   // fade the new conduit in
            }
        }

        public void Release() => inUse = false;

        public void Hide()
        {
            _alpha = 0f;
            SetActive(false);
        }

        public void FadeOut(float dt)
        {
            if (_alpha <= 0f) { SetActive(false); return; }
            float fade = _net.linkFadeTime > 0f ? dt / _net.linkFadeTime : 1f;
            _alpha = Mathf.Max(0f, _alpha - fade);
            if (_alpha <= 0f) SetActive(false);
            else ApplyColors();
        }

        public void Tick(float dt, Vector3 a, Vector3 b, bool live)
        {
            a.z = 0f; b.z = 0f;

            float target = live ? 1f : 0f;
            float fade = _net.linkFadeTime > 0f ? dt / _net.linkFadeTime : 1f;
            _alpha = Mathf.MoveTowards(_alpha, target, fade);

            if (_alpha <= 0f) { SetActive(false); return; }
            SetActive(true);

            Vector3 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.0001f) { SetActive(false); return; }

            // ── curve ──────────────────────────────────────────────────────────
            int n = Mathf.Clamp(_net.linkSegments, 4, 64);
            if (_points.Length != n) _points = new Vector3[n];

            Vector3 dir = delta / length;
            Vector3 perp = new Vector3(-dir.y, dir.x, 0f);
            float t = Time.time;

            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                float bow = Mathf.Sin(u * Mathf.PI);                  // 0 at ends
                float wobble = Mathf.Sin(u * Mathf.PI * 3f + t * _net.wobbleSpeed + _phase);
                float offset = bow * (_net.sagAmount * _side + _net.wobbleAmount * wobble);
                _points[i] = a + dir * (length * u) + perp * offset;
            }

            if (_beam.positionCount != n) { _beam.positionCount = n; _glow.positionCount = n; }
            _beam.SetPositions(_points);
            _glow.SetPositions(_points);

            // ── width, flow, sorting ───────────────────────────────────────────
            float breathe = 0.85f + 0.15f * Mathf.Sin(t * 3f + _phase);
            float w = Mathf.Max(0.01f, _net.linkWidth) * breathe;
            _beam.startWidth = _beam.endWidth = w;
            _glow.startWidth = _glow.endWidth = w * Mathf.Max(1f, _net.glowWidthScale);
            _glow.enabled = _net.showGlow;

            float tile = Mathf.Max(0.1f, _net.flowTileLength);
            _beamMat.mainTextureScale = new Vector2(Mathf.Max(1f, length / tile), 1f);
            _scroll -= _net.flowSpeed * dt / tile;          // negative = travels A→B
            _scroll = Mathf.Repeat(_scroll, 1f);
            _beamMat.mainTextureOffset = new Vector2(_scroll, 0f);

            int order = _net.SortOrderFor(a, b);
            _beam.sortingOrder = order;
            _glow.sortingOrder = order - 1;

            ApplyColors();
            TickPackets(a, b, length, order);
        }

        private void ApplyColors()
        {
            Color c = _isCoreLink ? _net.coreLinkColor : _net.towerLinkColor;
            float baseA = c.a * _alpha;

            // Hot near the generator, settling into the link colour at the receiver.
            _beamColorKeys[0] = new GradientColorKey(Color.Lerp(c, Color.white, 0.35f), 0f);
            _beamColorKeys[1] = new GradientColorKey(c, 1f);
            _beamAlphaKeys[0] = new GradientAlphaKey(baseA * 0.15f, 0f);
            _beamAlphaKeys[1] = new GradientAlphaKey(baseA, 0.18f);
            _beamAlphaKeys[2] = new GradientAlphaKey(baseA, 0.82f);
            _beamAlphaKeys[3] = new GradientAlphaKey(baseA * 0.15f, 1f);
            _gradBeam.SetKeys(_beamColorKeys, _beamAlphaKeys);
            _beam.colorGradient = _gradBeam;

            _glowColorKeys[0] = new GradientColorKey(c, 0f);
            _glowColorKeys[1] = new GradientColorKey(c, 1f);
            _glowAlphaKeys[0] = new GradientAlphaKey(0f, 0f);
            _glowAlphaKeys[1] = new GradientAlphaKey(baseA * 0.28f, 0.5f);
            _glowAlphaKeys[2] = new GradientAlphaKey(0f, 1f);
            _gradGlow.SetKeys(_glowColorKeys, _glowAlphaKeys);
            _glow.colorGradient = _gradGlow;
        }

        private void TickPackets(Vector3 a, Vector3 b, float length, int order)
        {
            int want = _net.showPackets ? Mathf.Clamp(_net.packetsPerLink, 0, 6) : 0;

            while (_packets.Count < want)
            {
                var go = new GameObject("Packet");
                go.transform.SetParent(_root.transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = DotSprite;
                sr.sharedMaterial = CreateLineMaterial(true);
                _packets.Add(sr);
            }
            for (int i = want; i < _packets.Count; i++)
                if (_packets[i] != null) _packets[i].enabled = false;

            if (want == 0) return;

            Color c = _isCoreLink ? _net.coreLinkColor : _net.towerLinkColor;
            float travel = Mathf.Repeat(Time.time * _net.flowSpeed / Mathf.Max(0.01f, length), 1f);

            for (int i = 0; i < want; i++)
            {
                var sr = _packets[i];
                if (sr == null) continue;

                float u = Mathf.Repeat(travel + i / (float)want, 1f);
                sr.transform.position = SampleCurve(u);
                sr.enabled = true;
                sr.sortingOrder = order + 1;

                // Fade in/out at the endpoints so packets do not pop.
                float ends = Mathf.Sin(u * Mathf.PI);
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 6f + i);
                var col = c;
                col.a = c.a * _alpha * ends;
                sr.color = col;

                float s = _net.packetSize * (0.7f + 0.5f * ends) * pulse;
                sr.transform.localScale = Vector3.one * s;
            }
        }

        private Vector3 SampleCurve(float u)
        {
            int n = _points.Length;
            if (n < 2) return _points.Length > 0 ? _points[0] : Vector3.zero;

            float f = Mathf.Clamp01(u) * (n - 1);
            int i0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, n - 2);
            return Vector3.Lerp(_points[i0], _points[i0 + 1], f - i0);
        }

        private void SetActive(bool on)
        {
            if (_root != null && _root.activeSelf != on) _root.SetActive(on);
        }

        public void Destroy()
        {
            for (int i = 0; i < _packets.Count; i++)
                if (_packets[i] != null) SafeDestroy(_packets[i].sharedMaterial);
            _packets.Clear();

            SafeDestroy(_beamMat);
            SafeDestroy(_glowMat);
            if (_root != null) SafeDestroy(_root);
        }
    }

    #endregion
}


// GeneratorLinkReceiver — attached by the network to every powered tower.
// Read by EnergyManager.GetDecayRate (same pattern as TowerCommanderBoost /
// GeneratorProximityBoost / TowerTetherDecayBoost) and ticks the repair trickle.
// Purely runtime: nothing here is serialized, so save/resume and wave rewinds are
// unaffected — the network simply re-adds it on the next refresh.
[DisallowMultipleComponent]
public class GeneratorLinkReceiver : MonoBehaviour
{
    public int LinkCount { get; private set; }
    public float RegenPerSecond { get; private set; }
    public float DecayMultiplier { get; private set; } = 1f;

    /// EnergyManager hook. 0 while powered = decay fully compensated.
    public float GetDecayMultiplier() => DecayMultiplier;

    private Tower _tower;
    private int _lastPass = int.MinValue;
    private int _pendingLinks;
    private float _pendingRegen;

    private float _accum, _flushTimer;

    private GameObject _auraObject;
    private SpriteRenderer _auraRenderer;
    private SpriteRenderer _towerRenderer;
    private float _auraFade, _auraAngle;

    void Awake()
    {
        _tower = GetComponent<Tower>();
        ResolveTowerRenderer();
        if (_tower == null) { enabled = false; return; }
    }

    /// Tower normally puts its SpriteRenderer on its own GameObject, but a prefab
    /// using `usePrefabVisuals` can have it on a child. Resolved once, and never in
    /// a way that could latch onto our own aura renderer.
    private void ResolveTowerRenderer()
    {
        if (_towerRenderer != null) return;

        _towerRenderer = GetComponent<SpriteRenderer>();
        if (_towerRenderer != null) return;

        var all = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (_auraRenderer != null && all[i] == _auraRenderer) continue;
            if (_auraObject != null && all[i].gameObject == _auraObject) continue;
            _towerRenderer = all[i];
            return;
        }
    }

    internal void Accumulate(int passId, float regenPerLink)
    {
        if (_lastPass != passId) { _lastPass = passId; _pendingLinks = 0; _pendingRegen = 0f; }
        _pendingLinks++;
        _pendingRegen += Mathf.Max(0f, regenPerLink);
    }

    internal void Commit(int passId, float maxRegen, bool cancelDecay)
    {
        if (_lastPass != passId) { _pendingLinks = 0; _pendingRegen = 0f; }

        LinkCount = _pendingLinks;
        RegenPerSecond = maxRegen > 0f ? Mathf.Min(_pendingRegen, maxRegen) : _pendingRegen;
        DecayMultiplier = (LinkCount > 0 && cancelDecay) ? 0f : 1f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        TickRegen(dt);
        TickAura(dt);
    }

    private void TickRegen(float dt)
    {
        if (_tower == null || LinkCount <= 0 || RegenPerSecond <= 0f) { _accum = 0f; return; }

        var net = GeneratorEnergyNetwork.Instance;
        bool revive = net == null || net.reviveDepletedTowers;

        // A tower sitting at 0 energy is "destroyed" as far as Tower.IsDestroyed()
        // is concerned, but the GameObject is alive and SupplyEnergy will bring it
        // back (it calls EnableTower once energy goes positive). That revival is
        // exactly the requested behaviour, but it is gated so it can be switched off.
        if (!revive && _tower.IsEnergyDepleted()) { _accum = 0f; return; }

        if (_tower.GetEnergy() >= _tower.GetMaxEnergy()) { _accum = 0f; _flushTimer = 0f; return; }

        _accum += RegenPerSecond * dt;
        _flushTimer += dt;

        // Flushed on a tick, not per frame: SupplyEnergy fires OnEnergyChanged and
        // repaints the energy bar, and waking that every frame for a fractional
        // change is pure overhead.
        if (_flushTimer < 0.1f) return;
        _flushTimer = 0f;
        if (_accum > 0f) _tower.SupplyEnergy(_accum);
        _accum = 0f;
    }

    private void TickAura(float dt)
    {
        var net = GeneratorEnergyNetwork.Instance;
        bool want = LinkCount > 0 && net != null && net.showReceiverAura;

        _auraFade = Mathf.MoveTowards(_auraFade, want ? 1f : 0f, dt * 3f);

        if (_auraFade <= 0f)
        {
            if (_auraObject != null) _auraObject.SetActive(false);
            return;
        }

        if (_auraObject == null) BuildAura();
        if (_auraObject == null || net == null) return;

        if (!_auraObject.activeSelf) _auraObject.SetActive(true);

        float t = Time.time;
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.Max(0.01f, net.receiverAuraPulseSpeed));

        Color c = net.receiverAuraColor;
        c.a = net.receiverAuraColor.a * _auraFade * (0.45f + 0.55f * pulse);
        _auraRenderer.color = c;

        // The ring sprite is authored at 1×1 world units, so localScale has to undo
        // the tower's own transform scale (Tower sets localScale = spriteScale).
        float world = MeasureTowerWorldSize() * Mathf.Max(0.1f, net.receiverAuraScale);
        float parent = Mathf.Max(0.0001f, transform.lossyScale.x);
        float s = (world / parent) * (1f + 0.06f * pulse);
        _auraObject.transform.localScale = Vector3.one * s;
        _auraObject.transform.localPosition = net.receiverAuraOffset;

        // Spin in WORLD space. Combat towers rotate their own transform to track a
        // target, and a ring parented to that would be dragged around by the turret
        // instead of turning at its own steady rate.
        _auraAngle = Mathf.Repeat(_auraAngle + net.receiverAuraSpin * dt, 360f);
        _auraObject.transform.rotation = Quaternion.Euler(0f, 0f, _auraAngle);

        // The tower is y-sorted every frame by YSortEntity, so track its order.
        ResolveTowerRenderer();
        if (_towerRenderer != null)
        {
            _auraRenderer.sortingLayerID = _towerRenderer.sortingLayerID;
            _auraRenderer.sortingOrder = _towerRenderer.sortingOrder +
                                         (net.receiverAuraBehindTower ? -1 : 1);
        }
    }

    private float MeasureTowerWorldSize()
    {
        ResolveTowerRenderer();
        if (_towerRenderer != null && _towerRenderer.sprite != null)
        {
            // sprite.bounds is the UNROTATED local size. Renderer.bounds would be the
            // rotated world AABB, which swells and shrinks as a turret tracks a
            // target — the ring would visibly breathe with the tower's aim.
            Vector3 local = _towerRenderer.sprite.bounds.size;
            Vector3 scale = _towerRenderer.transform.lossyScale;
            float m = Mathf.Max(Mathf.Abs(local.x * scale.x), Mathf.Abs(local.y * scale.y));
            if (m > 0.01f && !float.IsNaN(m) && !float.IsInfinity(m)) return m;
        }
        return 1f;
    }

    private void BuildAura()
    {
        _auraObject = new GameObject("GeneratorLinkAura");
        _auraObject.transform.SetParent(transform, false);
        _auraObject.transform.localPosition = Vector3.zero;
        _auraObject.transform.localRotation = Quaternion.identity;

        _auraRenderer = _auraObject.AddComponent<SpriteRenderer>();
        _auraRenderer.sprite = GeneratorEnergyNetwork.RingSprite;
        _auraRenderer.sharedMaterial = GeneratorEnergyNetwork.CreateLineMaterial(true);

        var net = GeneratorEnergyNetwork.Instance;
        _auraRenderer.color = net != null ? net.receiverAuraColor : Color.cyan;

        if (_towerRenderer != null)
        {
            _auraRenderer.sortingLayerID = _towerRenderer.sortingLayerID;
            _auraRenderer.sortingOrder = _towerRenderer.sortingOrder - 1;
        }
    }

    void OnDestroy()
    {
        if (_auraRenderer != null) GeneratorEnergyNetwork.SafeDestroy(_auraRenderer.sharedMaterial);
        if (_auraObject != null) GeneratorEnergyNetwork.SafeDestroy(_auraObject);
    }
}


// ─────────────────────────────────────────────────────────────────────────────
// GeneratorSelfSustain — zeroes a generator's own generation self-consumption so
// that "zero decay" really does mean "never loses energy".
//
// Tower.generatorSelfConsumption is a public serialized field on the prefab, so
// the original is cached here and restored the moment the option is turned off
// or the network is torn down. Nothing is written back to the prefab asset.
// ─────────────────────────────────────────────────────────────────────────────
[DisallowMultipleComponent]
public class GeneratorSelfSustain : MonoBehaviour
{
    private Tower _tower;
    private float _original;
    private bool _applied;

    public void Apply()
    {
        if (_applied) return;
        if (_tower == null) _tower = GetComponent<Tower>();
        if (_tower == null) return;

        _original = _tower.generatorSelfConsumption;
        _tower.generatorSelfConsumption = 0f;
        _applied = true;
    }

    public void Restore()
    {
        if (!_applied) return;
        if (_tower != null) _tower.generatorSelfConsumption = _original;
        _applied = false;
    }

    void OnDisable() => Restore();
    void OnDestroy() => Restore();
}



