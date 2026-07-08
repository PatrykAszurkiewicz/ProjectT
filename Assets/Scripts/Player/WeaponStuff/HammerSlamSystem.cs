using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Battle Hammer ground-slam subsystem. Hold left-click to CHARGE

public class HammerSlamSystem
{
    private readonly Weapon _weapon;
    private readonly WeaponData _data;

    // The active slam/charge runner. The coroutine MUST NOT run on the Weapon
    // MonoBehaviour: Weapon.ApplySwapCooldown() calls StopAllCoroutines(), and
    // any weapon equip/swap during the hammer's wind-up would kill the slam
    // before it reaches the damage step.
    private HammerSlamRunner _activeRunner;

    private static Sprite _pixelSprite;
    private static Sprite _ghostSprite;
    private static string _ghostSpritePathLoaded;

    public bool IsSlamming => _activeRunner != null && _activeRunner.IsRunning;
    public bool IsCharging => _activeRunner != null && _activeRunner.IsCharging;

    public HammerSlamSystem(Weapon weapon, WeaponData data)
    {
        _weapon = weapon;
        _data = data;
    }

    public void Cleanup()
    {
        if (_activeRunner != null)
        {
            _activeRunner.Abort();
            _activeRunner = null;
        }
    }

    // Begin charging a hammer swing (button pressed). If charging is disabled
    // on the WeaponData, the runner fires an immediate (un-charged) slam.
    public void BeginCharge()
    {
        if (_weapon == null || _data == null) return;
        if (IsSlamming || IsCharging) return;

        var go = new GameObject("HammerSlamRunner");
        _activeRunner = go.AddComponent<HammerSlamRunner>();
        _activeRunner.BeginCharge(_weapon, _data);
    }

    // Release the charged swing (button released). Returns true if a slam was
    // actually triggered (i.e. a charge was in progress).
    public bool ReleaseCharge()
    {
        if (_activeRunner == null || !_activeRunner.IsCharging) return false;
        _activeRunner.ReleaseCharge();
        return true;
    }

    // Cancel an in-progress charge WITHOUT slamming (e.g. out of stamina).
    public void CancelCharge()
    {
        if (_activeRunner != null && _activeRunner.IsCharging)
        {
            _activeRunner.Abort();
            _activeRunner = null;
        }
    }

    // Fire an immediate, fully-uncharged slam. Kept for any non-charge callers.
    public bool PerformSlam()
    {
        if (_weapon == null || _data == null) return false;
        if (IsSlamming || IsCharging) return false;

        var go = new GameObject("HammerSlamRunner");
        _activeRunner = go.AddComponent<HammerSlamRunner>();
        _activeRunner.BeginInstantSlam(_weapon, _data);
        return true;
    }

    //  SEMI-TRANSPARENT GHOST HAMMER SPRITE 

    // Loads (and caches) the ghost hammer sprite from Resources using the path
    // configured on the WeaponData. Returns null if the path is empty / wrong.
    internal static Sprite GetGhostSprite(string resourcesPath)
    {
        if (string.IsNullOrEmpty(resourcesPath)) return null;
        if (_ghostSprite != null && _ghostSpritePathLoaded == resourcesPath)
            return _ghostSprite;

        _ghostSprite = Resources.Load<Sprite>(resourcesPath);
        _ghostSpritePathLoaded = resourcesPath;
        if (_ghostSprite == null)
            Debug.LogWarning($"[HammerSlamSystem] Ghost hammer sprite not found at " +
                             $"Resources/{resourcesPath}. The swing will play without it.");
        return _ghostSprite;
    }

    //  SHARED PIXEL SPRITE 
    internal static Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return _pixelSprite;
    }

    // A soft radial-gradient disc — used for dust puffs and glows so they look
    // like soft clouds instead of hard-edged squares.
    private static Sprite _softDisc;
    internal static Sprite GetSoftDiscSprite()
    {
        if (_softDisc != null) return _softDisc;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        float c = (S - 1) * 0.5f;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                // Soft falloff: opaque core, feathered edge.
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a); // smoothstep
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        _softDisc = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _softDisc;
    }

    // A small set of irregular rock chunk sprites.
    private static Sprite[] _rockChunks;
    internal static Sprite GetRockChunkSprite()
    {
        if (_rockChunks == null) BakeRockChunks();
        return _rockChunks[Random.Range(0, _rockChunks.Length)];
    }

    private static void BakeRockChunks()
    {
        const int variants = 6;
        const int S = 32;                 // small texture — debris is tiny on screen
        _rockChunks = new Sprite[variants];
        float c = (S - 1) * 0.5f;

        for (int v = 0; v < variants; v++)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Build a jagged silhouette: per-angle radius wobble so the outline
            // is angular and asymmetric, like a fractured stone.
            const int spokes = 11;
            float[] radii = new float[spokes];
            for (int s = 0; s < spokes; s++)
                radii[s] = Random.Range(0.52f, 0.96f);

            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - c) / c;
                    float dy = (y - c) / c;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Interpolate the silhouette radius for this pixel's angle.
                    float ang = Mathf.Atan2(dy, dx);
                    if (ang < 0f) ang += Mathf.PI * 2f;
                    float fs = ang / (Mathf.PI * 2f) * spokes;
                    int s0 = Mathf.FloorToInt(fs) % spokes;
                    int s1 = (s0 + 1) % spokes;
                    float edge = Mathf.Lerp(radii[s0], radii[s1], fs - Mathf.Floor(fs));

                    if (dist > edge)
                    {
                        px[y * S + x] = Color.clear;
                        continue;
                    }

                    // Baked shading: brighter toward the top, darker at the base,
                    // with a soft 1px feather at the silhouette edge for anti-alias.
                    float topLight = Mathf.Lerp(0.55f, 1.15f, (dy + 1f) * 0.5f);
                    // Slight inner core darkening so the rock isn't a flat fill.
                    float coreShade = Mathf.Lerp(0.82f, 1f, dist / Mathf.Max(edge, 0.001f));
                    float lum = Mathf.Clamp01(topLight * coreShade);
                    float aa = Mathf.Clamp01((edge - dist) * c); // feather last pixel
                    px[y * S + x] = new Color(lum, lum, lum, aa);
                }

            tex.SetPixels(px);
            tex.Apply();
            _rockChunks[v] = Sprite.Create(tex, new Rect(0, 0, S, S),
                                           new Vector2(0.5f, 0.5f), S);
        }
    }
}

//  SLAM RUNNER — hosts the charge + swing coroutine on its own GameObject

public class HammerSlamRunner : MonoBehaviour
{
    private Weapon _weapon;
    private WeaponData _data;

    // Co-op: this hammer's OWNING player's aim, resolved from the weapon's parent
    // hierarchy. Used so the slam follows THIS player's cursor/stick instead of
    // whichever player last won the global PlayerAim.Instance.
    private PlayerAim _ownerAim;

    private bool _running;        // a slam (windup→impact→recoil) is playing
    private bool _charging;       // currently holding the charge
    private bool _releaseQueued;  // release requested while still charging
    private float _chargeFactor;  // 0..1 — how full the charge was at release

    // The semi-transparent ghost hammer that plays the swing animation.
    private GameObject _ghost;
    private SpriteRenderer _ghostSr;
    private Transform _ghostPivot; // rotates; the sprite hangs off it

    // The charge build-up VFX (separate GameObject — cleaned up on abort).
    private HammerChargeVFX _chargeVfx;

    public bool IsRunning => _running;
    public bool IsCharging => _charging;

    //  ENTRY POINTS 
    // Hold-to-charge entry. 
    public void BeginCharge(Weapon weapon, WeaponData data)
    {
        _weapon = weapon;
        _data = data;

        if (!data.hammerChargeEnabled)
        {
            // Charging disabled on this weapon → behave like an instant slam.
            BeginInstantSlam(weapon, data);
            return;
        }

        _charging = true;
        StartCoroutine(ChargeThenSlamRoutine());
    }

    // Immediate, uncharged slam (charge disabled, or a non-charge caller).
    public void BeginInstantSlam(Weapon weapon, WeaponData data)
    {
        _weapon = weapon;
        _data = data;
        _chargeFactor = 0f;
        _running = true;
        StartCoroutine(SlamRoutine());
    }

    // Button released — let the charge loop fall through into the slam.
    public void ReleaseCharge()
    {
        _releaseQueued = true;
    }

    // Cancel everything (hammer unequipped mid-swing, or out of stamina).
    public void Abort()
    {
        StopAllCoroutines();
        DestroyGhost();
        // The charge VFX is a separate GameObject — destroy it too so its
        // mote-spawning loop doesn't leak when a charge is cancelled.
        if (_chargeVfx != null)
        {
            Destroy(_chargeVfx.gameObject);
            _chargeVfx = null;
        }
        _running = false;
        _charging = false;
        if (this != null) Destroy(gameObject);
    }

    //  CHARGE and SLAM 

    private IEnumerator ChargeThenSlamRoutine()
    {
        SpawnGhost();

        float chargeTime = Mathf.Max(0.05f, _data.hammerChargeTime);
        float held = 0f;

        // Charge VFX — a ring that fills as the charge builds, shown at the
        // current reach point so the player sees where the slam will land.
        var chargeFx = new GameObject("HammerChargeFX");
        var charge = chargeFx.AddComponent<HammerChargeVFX>();
        _chargeVfx = charge;
        charge.Begin(_data);

        //  HEAVY LIFT
        StartChargeSound();

        //  THE HEAVE 
        yield return AnimateSwing(0.10f, 0f, 0.16f, 0.93f, EaseOutCubic);   // brace dip
        yield return AnimateSwing(0.16f, 0.16f, -0.05f, 1.05f, EaseOutCubic); // haul up

        //  CHARGING
        while (!_releaseQueued)
        {
            held += Time.deltaTime;
            float c = Mathf.Clamp01(held / chargeTime);
            charge.SetCharge(c);

            // Keep tracking the player + aim direction every frame so the
            // wind-up pose always points toward the cursor.
            UpdateRigOrientation();
            chargeFx.transform.position = ResolveImpactPoint();

            // HEAVINESS 1 — tremble escalates the whole time. Even at zero
            // charge the hammer shudders a little; at full charge it shakes
            // hard, like the player can barely hold it.
            _shakeAmount = Mathf.Lerp(0.35f, 1.5f, c);

            // HEAVINESS 2 — a heavy "heartbeat" pulse. The hammer breathes,
            // and the beat quickens as the charge fills (1.8 Hz → 5 Hz).
            float beatHz = Mathf.Lerp(1.8f, 5f, c);
            float beat = Mathf.Sin(Time.unscaledTime * beatHz * Mathf.PI * 2f);
            // Sharpen the sine into a thumping pulse (fast swell, slow fall).
            float thump = Mathf.Pow(Mathf.Clamp01(beat * 0.5f + 0.5f), 2.2f);
            _pulseScale = 1f + thump * Mathf.Lerp(0.05f, 0.16f, c);

            // Drift a touch further back as charge builds (loading the swing).
            float swing = -0.05f - 0.06f * c;
            PoseGhost(swing, Mathf.Lerp(1f, 1.13f, c));

            // Pulse the charge sound's pitch with the heartbeat so the audio
            // throbs in time with the visual.
            UpdateChargeSound(c, thump);

            yield return null;
        }

        _chargeFactor = Mathf.Clamp01(held / chargeTime);
        _charging = false;
        _running = true;

        _pulseScale = 1f;
        charge.Release(_chargeFactor);
        _chargeVfx = null;

        yield return SlamSequence();
    }

    private IEnumerator SlamRoutine()
    {
        SpawnGhost();
        UpdateRigOrientation();
        PoseGhost(0f, 1f); // start at the raised position
        yield return SlamSequence();
    }

    // The shared windup → arc-swing → impact → recoil sequence.
    private IEnumerator SlamSequence()
    {
        float windup = Mathf.Max(0.05f, _data.hammerWindup);
        float rearTime = windup * 0.62f;
        float dropTime = windup - rearTime;

        // Lock in the aim for this swing and show the telegraph where it lands.
        UpdateRigOrientation();
        Vector3 impact = ResolveImpactPoint();
        float radius = SlamRadius();
        var telegraph = new GameObject("HammerTelegraph");
        telegraph.transform.position = impact;
        telegraph.AddComponent<HammerTelegraphRing>()
                 .Play(radius, _data.hammerShockwaveColor, windup);

        float startSwing = _swing;

        // WIND-UP: heave the hammer up to the top of the arc. It shakes hard
        // here — the strain of lifting a heavy weapon overhead. A charged
        // swing trembles even harder.
        _shakeAmount = Mathf.Lerp(0.8f, 1.7f, _chargeFactor);
        // The "stretched" charge/lift sound rises in pitch and then cuts as
        // the hammer reaches the top and begins to fall.
        yield return AnimateSwing(rearTime, startSwing, -0.14f, 1.16f, EaseOutCubic);

        // The hammer is at the apex — kill the tremble, the swing is committed.
        _shakeAmount = 0f;
        StopChargeSound();
        // A whoosh as the hammer is thrown down (pitch-shifted by charge so a
        // big swing sounds deeper / heavier).
        PlaySwingWhoosh();

        // Held-breath (charged only) is computed up front so we know exactly how
        // long until the visual ground contact, and can start the impact SFX
        // early enough that its transient lands ON the hit — see hammerHitSfxLead.
        float holdBreath = (_chargeFactor > 0.15f) ? Mathf.Lerp(0f, 0.05f, _chargeFactor) : 0f;
        float timeToImpact = dropTime + holdBreath;
        float sfxLead = Mathf.Clamp(_data.hammerHitSfxLead, 0f, timeToImpact);
        StartCoroutine(PlayHammerHitSfxAfter(timeToImpact - sfxLead));

        // DROP: whip it down through the arc, accelerating into the ground.
        yield return AnimateSwing(dropTime, -0.14f, 1f, 1.22f, EaseInQuart);

        // HEAVINESS — a tiny "held breath" right before a charged hit lands:
        // a micro freeze-frame at the bottom of the swing. Scales with charge,
        // skipped entirely on a quick tap.
        if (holdBreath > 0f)
        {
            float held = 0f;
            while (held < holdBreath) { held += Time.unscaledDeltaTime; yield return null; }
        }

        // IMPACT — re-resolve so a moving player still lands the slam correctly.
        UpdateRigOrientation();
        impact = ResolveImpactPoint();

        ApplySlamDamage(impact);
        SpawnSlamVFX(impact);
        ApplyImpactFeel();

        // RECOIL: a small overshoot past the ground, settle, fade out. The
        // hammer judders briefly on impact, then the shake dies away.
        _shakeAmount = Mathf.Lerp(0.3f, 0.9f, _chargeFactor);
        StartCoroutine(FadeOutGhost(0.22f));
        yield return AnimateSwing(0.09f, 1f, 0.86f, 1.0f, EaseOutCubic);
        _shakeAmount = 0f;
        yield return AnimateSwing(0.16f, 0.86f, 0.55f, 1.0f, EaseOutBack);

        DestroyGhost();
        _running = false;
        Destroy(gameObject);
    }

    // Plays the ground-slam SFX. Kicked off before the DROP so a non-zero
    // hammerHitSfxLead starts the event early and its impact transient lands on
    // the visual ground contact (compensates for lead-in / attack baked into the
    // FMOD event). With lead = 0 the wait equals time-to-impact, so it fires
    // exactly at contact. Counts scaled time to stay locked to the swing (which
    // also uses scaled time); timescale is 1 pre-impact, so this matches the
    // held-breath's unscaled wait too.
    private IEnumerator PlayHammerHitSfxAfter(float delay)
    {
        float t = 0f;
        while (t < delay) { t += Time.deltaTime; yield return null; }

        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.hammerHit.IsNull)
        {
            AudioManager.instance.PlaySFX(FMODEvents.instance.hammerHit, ResolveImpactPoint());
        }
    }

    //  GHOST HAMMER (directional reach rig) 
    private Transform _arm;       // rotates through the swing arc (local frame)
    private float _swing;         // current swing parameter
    private float _aimAngleDeg;   // pivot rotation = aim direction
    private bool _aimFlipped;     // true when aiming leftwards (sprite flipped)
    private float _ghostBaseScale = -1f;

    // Tremble — how hard the hammer shakes from "straining under the weight".
    // Set by the charge/lift code; PoseGhost turns it into a jittery offset.
    private float _shakeAmount;
    // An extra uniform scale "breathing" pulse layered on top of the pose.
    private float _pulseScale = 1f;
    // Persistent random seed so the tremble noise is smooth, not chaotic.
    private float _shakeSeedX;
    private float _shakeSeedY;

    // A held FMOD instance for the pitch-stretched lift / charge sound.
    private FMOD.Studio.EventInstance _chargeSound;
    private bool _hasChargeSound;

    private void SpawnGhost()
    {
        // Random per-swing seeds so the tremble noise is unique each swing.
        _shakeSeedX = Random.value * 100f;
        _shakeSeedY = Random.value * 100f + 50f;

        // Pivot at the player; rotated to face the aim direction.
        _ghostPivot = new GameObject("HammerGhostPivot").transform;

        // Arm: child of the pivot. The swing arc rotates THIS.
        _arm = new GameObject("HammerGhostArm").transform;
        _arm.SetParent(_ghostPivot, false);

        // The hammer sprite hangs off the end of the arm.
        _ghost = new GameObject("HammerGhost");
        _ghost.transform.SetParent(_arm, false);

        _ghostSr = _ghost.AddComponent<SpriteRenderer>();
        Sprite s = HammerSlamSystem.GetGhostSprite(_data.hammerGhostSpritePath);
        _ghostSr.sprite = s;
        _ghostSr.sortingLayerName = "Default";
        _ghostSr.sortingOrder = 5300;

        Color c = Color.white;
        c.a = Mathf.Clamp01(_data.hammerGhostAlpha);
        _ghostSr.color = c;

        if (s != null)
        {
            float spriteH = Mathf.Max(0.01f, s.bounds.size.y);
            float scale = Mathf.Max(0.1f, _data.hammerGhostSize) / spriteH;
            _ghost.transform.localScale = new Vector3(scale, scale, 1f);
            _ghostBaseScale = scale;
        }
        else
        {
            _ghostSr.sprite = HammerSlamSystem.GetPixelSprite();
            _ghost.transform.localScale = new Vector3(0.35f, _data.hammerGhostSize, 1f);
            _ghostBaseScale = 0.35f;
        }

        // The hammer sprite hangs off the END of the arm, along the arm's
        // local +X. The art points "up", so we rotate the sprite -90° to aim
        // the head outward along the arm. The +X offset is set per-frame in
        // PoseGhost (it equals the reach distance).
        _ghost.transform.localPosition = Vector3.zero;
        _ghost.transform.localEulerAngles = new Vector3(0f, 0f, -90f);

        UpdateRigOrientation();
        PoseGhost(0f, 1f);
    }

    // Re-aim the rig at the player + cursor. Called every frame during charge
    // and at each impact so the swing tracks a moving player / moving aim.
    private void UpdateRigOrientation()
    {
        if (_ghostPivot == null) return;

        Vector3 playerPos = ResolvePlayerPos();
        Vector2 aim = ResolveAimDirection();

        _ghostPivot.position = playerPos;
        _aimAngleDeg = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;

        // Aiming leftwards → flip the sprite vertically so the hammer head is
        // never upside-down (standard 2D sprite mirroring).
        _aimFlipped = Mathf.Abs(_aimAngleDeg) > 90f;

        // Rotate the pivot so local +X = aim direction. When flipped we mirror
        // the frame so "up" in art space stays up on screen.
        _ghostPivot.localEulerAngles = new Vector3(0f, 0f, _aimAngleDeg);
        _ghostPivot.localScale = new Vector3(1f, _aimFlipped ? -1f : 1f, 1f);
    }

    // Map the swing parameter to the arm's arc angle + reach, and pose it.
    // The arc sweeps in the pivot's aim-aligned local frame:
    //   swing 0 → arm raised up & back  (≈ 150° from local forward)
    //   swing 1 → arm down at the reach point (≈ -8° below local forward)
    private void PoseGhost(float swing, float scaleMul)
    {
        _swing = swing;
        if (_ghostPivot == null || _arm == null || _ghost == null) return;

        // Arc angle in local space. 0° = straight along the aim direction
        // (local +X). The arm rotates around the pivot (= the player).
        float armAngle = Mathf.Lerp(150f, -8f, swing);

        // ── HEAVINESS: tremble ──
        // When _shakeAmount > 0 the hammer shakes as if straining under its own
        // weight: a smooth Perlin jitter on the arm angle plus a tiny rotational
        // wobble. Driven by the lift / charge code.
        float trembleAngle = 0f;
        if (_shakeAmount > 0.0001f)
        {
            float tNoise = Time.unscaledTime * 26f;
            float nx = Mathf.PerlinNoise(_shakeSeedX, tNoise) - 0.5f;
            float ny = Mathf.PerlinNoise(_shakeSeedY, tNoise) - 0.5f;
            // Most of the shake is an angular shudder; a sharp sine adds a
            // higher-frequency "judder" so it reads as heavy, not floaty.
            trembleAngle = (nx * 9f + Mathf.Sin(tNoise * 1.7f) * ny * 5f) * _shakeAmount;
        }
        _arm.localEulerAngles = new Vector3(0f, 0f, armAngle + trembleAngle);

        // The hammer sprite sits at the arm's tip, `reach` units out along the
        // arm's local +X. Since the arm is rotated by armAngle, the head ends
        // up at  pivot + Rot(aim) * Rot(armAngle) * (reach, 0)  — i.e. it lands
        // exactly on the reach point when armAngle hits 0°.
        float reach = CurrentReach();
        // Pull the sprite in by half its height so the HEAD (not the centre)
        // reaches the impact point.
        float headInset = _data.hammerGhostSize * 0.5f;
        float along = Mathf.Max(0f, reach - headInset);
        // A small positional shudder along/across the arm adds physical weight.
        if (_shakeAmount > 0.0001f)
        {
            float tN = Time.unscaledTime * 30f;
            along += (Mathf.PerlinNoise(_shakeSeedY, tN) - 0.5f) * 0.18f * _shakeAmount;
        }
        _ghost.transform.localPosition = new Vector3(along, 0f, 0f);

        ApplyGhostScale(scaleMul);
    }

    private void ApplyGhostScale(float scaleMul)
    {
        if (_ghost == null) return;
        if (_ghostBaseScale < 0f) _ghostBaseScale = _ghost.transform.localScale.x;
        // _pulseScale is the charge "breathing" pulse, layered on the pose scale.
        float s = _ghostBaseScale * scaleMul * _pulseScale;
        // Preserve the sprite's aspect; only uniform-scale the art.
        _ghost.transform.localScale = new Vector3(s, s, 1f);
    }

    // Tween the swing parameter over `time`.
    private IEnumerator AnimateSwing(float time, float fromSwing, float toSwing,
                                     float scaleMul, System.Func<float, float> ease)
    {
        if (_ghostPivot == null || time <= 0f)
        {
            if (time > 0f) yield return new WaitForSeconds(time);
            _swing = toSwing;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < time)
        {
            elapsed += Time.deltaTime;
            float t = ease(Mathf.Clamp01(elapsed / time));
            if (_ghostPivot == null) yield break;
            // Keep tracking the player while the swing plays.
            UpdateRigOrientation();
            PoseGhost(Mathf.LerpUnclamped(fromSwing, toSwing, t), scaleMul);
            yield return null;
        }
        _swing = toSwing;
    }

    private IEnumerator FadeOutGhost(float time)
    {
        if (_ghostSr == null) yield break;
        Color baseCol = _ghostSr.color;
        float elapsed = 0f;
        while (elapsed < time)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / time);
            if (_ghostSr == null) yield break;
            Color c = baseCol; c.a = baseCol.a * (1f - t);
            _ghostSr.color = c;
            yield return null;
        }
    }

    private void DestroyGhost()
    {
        if (_ghostPivot != null) Destroy(_ghostPivot.gameObject);
        _ghostPivot = null;
        _arm = null;
        _ghost = null;
        _ghostSr = null;
    }

    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    private static float EaseInQuart(float t) => t * t * t * t;
    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    //  AIM / IMPACT POINT / CHARGE-SCALED STATS 

    private Vector3 ResolvePlayerPos()
    {
        if (_weapon == null) return transform.position;
        var ps = _weapon.GetComponentInParent<PlayerStats>();
        Vector3 p = ps != null ? ps.transform.position : _weapon.transform.position;
        p.z = 0f;
        return p;
    }

    // This hammer's OWN player's aim (resolved from the weapon hierarchy, then
    // cached). Retries until found so a transient early-null can't stick.
    private PlayerAim ResolveOwnerAim()
    {
        if (_ownerAim == null && _weapon != null)
            _ownerAim = _weapon.GetComponentInParent<PlayerAim>();
        return _ownerAim;
    }

    // The aim direction = the same unified aim (mouse OR gamepad) this player's
    // cursor uses. Resolved from THIS hammer's owner, not the global
    // PlayerAim.Instance — in co-op the global is whichever player spawned last,
    // which made a gamepad player's slam fly off in the other player's direction
    // while the mouse player (who happened to be the Instance) looked fine.
    private Vector2 ResolveAimDirection()
    {
        PlayerAim a = ResolveOwnerAim();
        if (a == null) a = PlayerAim.Instance;   // legacy single-player fallback
        if (a != null)
            return a.Direction;

        // Last-resort mouse fallback (no PlayerAim anywhere in the scene).
        var cam = Camera.main;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (cam == null || mouse == null) return Vector2.right;

        Vector3 mouseWorld = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        mouseWorld.z = 0f;
        Vector2 dir = (Vector2)(mouseWorld - ResolvePlayerPos());
        return dir.sqrMagnitude > 1e-5f ? dir.normalized : Vector2.right;
    }

    // Current reach distance (scales with charge up to hammerChargeReachBonus).
    private float CurrentReach()
    {
        float baseReach = Mathf.Max(0f, _data.hammerReachDistance);
        float mul = Mathf.Lerp(1f, Mathf.Max(1f, _data.hammerChargeReachBonus), _chargeFactor);
        return baseReach * mul;
    }

    // The world point the slam lands on: player position + aim * reach.
    private Vector3 ResolveImpactPoint()
    {
        Vector3 player = ResolvePlayerPos();
        float reach = CurrentReach();
        if (reach <= 0.001f) return player; // legacy: slam centred on player
        Vector2 aim = ResolveAimDirection();
        return player + (Vector3)(aim * reach);
    }

    // Slam radius grows with charge up to hammerChargeRadiusBonus at full charge.
    private float SlamRadius()
    {
        float baseR = Mathf.Max(0.1f, _data.hammerSlamRadius);
        float radiusMul = Mathf.Lerp(1f, Mathf.Max(1f, _data.hammerChargeRadiusBonus), _chargeFactor);
        return baseR * radiusMul;
    }

    // Damage multiplier from charge: 1 at no charge → 1 + hammerChargeBonus full.
    private float ChargeDamageMultiplier()
    {
        return 1f + _data.hammerChargeBonus * _chargeFactor;
    }

    //  AoE DAMAGE 
    private void ApplySlamDamage(Vector3 center)
    {
        float radius = SlamRadius();
        float slamDamage = _data.damage
                         * Mathf.Max(0f, _data.hammerAoEDamageMultiplier)
                         * ChargeDamageMultiplier();

        // Overlap query: OverlapCircleAll with no
        // ContactFilter and no LayerMask. This returns EVERY 2D collider that
        // overlaps the circle — triggers and non-triggers, on all layers
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius);

        // De-dupe: an enemy can carry several colliders; damage each once.
        var hitThisSlam = new HashSet<EnemyStats>();

        foreach (var col in hits)
        {
            if (col == null) continue;

            // Resolve the enemy from the collider or any of its parents.
            EnemyStats enemy = col.GetComponentInParent<EnemyStats>();
            if (enemy == null) continue;
            if (!hitThisSlam.Add(enemy)) continue;

            if (slamDamage > 0f)
            {
                float dmg = slamDamage;

                var parryEffect = enemy.GetComponent<ParryStunEffect>();
                if (parryEffect != null)
                    dmg *= parryEffect.DamageMultiplier;

                enemy.TakeDamage(dmg);
                CombatJuice.OnPlayerHitEnemy(enemy.gameObject, isMelee: true);
            }

            if (_data.hammerSlamKnockback > 0f)
            {
                var ec = enemy.GetComponent<EnemyController>();
                if (ec != null)
                {
                    // Knockback also gets a little stronger with charge.
                    float kb = _data.hammerSlamKnockback * Mathf.Lerp(1f, 1.4f, _chargeFactor);
                    Vector2 dir = (Vector2)(enemy.transform.position - center);
                    dir = dir.sqrMagnitude < 1e-4f
                        ? Random.insideUnitCircle.normalized
                        : dir.normalized;
                    ec.ApplyKnockback(dir, kb);
                }
            }
        }

        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlaySFX(FMODEvents.instance.meleeHit, center);
    }

    //  IMPACT FEEL 
    private void ApplyImpactFeel()
    {
        // Hitstop and shake both scale up with charge so a full charge lands hard.
        float chargeScale = Mathf.Lerp(1f, 1.5f, _chargeFactor);

        if (HitStop.Instance != null)
            HitStop.Instance.Freeze(_data.hammerHitStop * chargeScale, ignoreCooldown: true);

        if (CameraShake.Instance != null)
        {
            // Sharp jolt now; the lingering rumble is scheduled on THIS runner
            // (not the Weapon) so it can't be cancelled by a weapon swap.
            CameraShake.Instance.Shake(_data.hammerShakeJolt * chargeScale, 0.16f);
            StartCoroutine(DelayedRumble(chargeScale));
        }
    }

    private IEnumerator DelayedRumble(float chargeScale)
    {
        yield return new WaitForSeconds(0.17f);
        if (CameraShake.Instance != null)
            CameraShake.Instance.Shake(_data.hammerShakeRumble * chargeScale,
                                       _data.hammerShakeRumbleDuration);
    }

    //  VFX 
    private void SpawnSlamVFX(Vector3 center)
    {
        var root = new GameObject("HammerSlamVFX");
        root.transform.position = center;
        // The slam VFX scales itself with the charge factor for bigger blasts.
        root.AddComponent<HammerSlamVFX>().Play(_data, _chargeFactor);
    }

    //  AUDIO — pitch-stretch
    private void StartChargeSound()
    {
        if (FMODEvents.instance == null) return;
        try
        {
            _chargeSound = FMODUnity.RuntimeManager.CreateInstance(FMODEvents.instance.meleeSwing);
            if (!_chargeSound.isValid()) { _hasChargeSound = false; return; }

            _chargeSound.set3DAttributes(
                FMODUnity.RuntimeUtils.To3DAttributes(ResolvePlayerPos()));
            // Start deep and slow — the hammer being hauled up.
            _chargeSound.setPitch(0.55f);
            _chargeSound.start();
            _hasChargeSound = true;
        }
        catch
        {
            // Event missing / audio unavailable — fail silently, no sound.
            _hasChargeSound = false;
        }
    }

    // Called every charge frame: c = 0..1 charge, thump = 0..1 heartbeat pulse.
    private void UpdateChargeSound(float c, float thump)
    {
        if (!_hasChargeSound) return;
        if (!_chargeSound.isValid()) { _hasChargeSound = false; return; }

        // Pitch rises from a deep 0.55 toward ~1.05 as the charge fills, with
        // the heartbeat throbbing on top so the sound pulses with the visual.
        float basePitch = Mathf.Lerp(0.55f, 1.05f, c);
        float pitch = basePitch + thump * Mathf.Lerp(0.04f, 0.14f, c);
        _chargeSound.setPitch(pitch);
        _chargeSound.set3DAttributes(
            FMODUnity.RuntimeUtils.To3DAttributes(ResolvePlayerPos()));
    }

    private void StopChargeSound()
    {
        if (!_hasChargeSound) return;
        try
        {
            if (_chargeSound.isValid())
            {
                _chargeSound.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                _chargeSound.release();
            }
        }
        catch { /* already gone */ }
        _hasChargeSound = false;
    }

    // A one-shot "whoosh" as the hammer is thrown down. Deeper/heavier the more
    // the swing was charged (pitch shifts down with charge).
    private void PlaySwingWhoosh()
    {
        if (FMODEvents.instance == null) return;
        try
        {
            var whoosh = FMODUnity.RuntimeManager.CreateInstance(FMODEvents.instance.meleeSwing);
            if (!whoosh.isValid()) return;
            whoosh.set3DAttributes(
                FMODUnity.RuntimeUtils.To3DAttributes(ResolveImpactPoint()));
            // A full charge whooshes ~25% lower (heavier); a tap is near normal.
            whoosh.setPitch(Mathf.Lerp(1.0f, 0.75f, _chargeFactor));
            whoosh.start();
            whoosh.release(); // release immediately; FMOD frees it when it ends
        }
        catch { /* event missing — skip */ }
    }

    // Safety net — make sure the held charge sound never leaks if the runner
    // is destroyed (weapon swap, scene change) mid-charge.
    private void OnDestroy()
    {
        StopChargeSound();
    }
}

//  TELEGRAPH RING 
public class HammerTelegraphRing : MonoBehaviour
{
    public void Play(float radius, Color color, float lifetime)
    {
        StartCoroutine(Run(Mathf.Max(0.3f, radius), color, Mathf.Max(0.1f, lifetime)));
    }

    private IEnumerator Run(float radius, Color color, float lifetime)
    {
        var lr = gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = 56;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        // On the ground (above both backgrounds) but below the player, so the
        // telegraph reads as a faint mark on the floor.
        lr.sortingLayerName = "Default";
        lr.sortingOrder = 60;
        lr.startWidth = lr.endWidth = 0.03f;   // thin, understated hairline

        for (int i = 0; i < lr.positionCount; i++)
        {
            float a = (i / (float)lr.positionCount) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }

        float elapsed = 0f;
        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lifetime);
            // Fade in, then a quick pulse near the end as the slam lands.
            float alpha = Mathf.Lerp(0f, 0.14f, t);
            float pulse = 1f + 0.06f * Mathf.Sin(t * 28f);
            Color c = color; c.a = alpha;
            lr.startColor = lr.endColor = c;
            transform.localScale = Vector3.one * pulse;
            yield return null;
        }
        Destroy(gameObject);
    }
}

//  CHARGE VFX 
public class HammerChargeVFX : MonoBehaviour
{
    private const string SortLayer = "Default";
    private const int RingOrder = 70;      // on the ground (above both backgrounds), below the player
    private const int MoteOrder = 5250;    // motes float above the player

    private WeaponData _data;
    private LineRenderer _ring;
    private float _baseRadius;
    private float _charge;          // 0..1, set every frame by the runner
    private bool _released;
    private readonly List<Transform> _motes = new List<Transform>();

    public void Begin(WeaponData data)
    {
        _data = data;
        _baseRadius = Mathf.Max(0.3f, data.hammerSlamRadius);

        // Ground ring — starts wide and faint, tightens + brightens with charge.
        _ring = gameObject.AddComponent<LineRenderer>();
        _ring.useWorldSpace = false;
        _ring.loop = true;
        _ring.positionCount = 60;
        _ring.material = new Material(Shader.Find("Sprites/Default"));
        _ring.sortingLayerName = SortLayer;
        _ring.sortingOrder = RingOrder;
        _ring.startWidth = _ring.endWidth = 0.03f;

        StartCoroutine(SpawnMotes());
    }

    // Called every frame while charging (0 = just started, 1 = full charge).
    public void SetCharge(float c)
    {
        _charge = Mathf.Clamp01(c);
    }

    // Charge released — collapse the ring inward with a flash, then clean up.
    public void Release(float chargeFactor)
    {
        if (_released) return;
        _released = true;
        StartCoroutine(ReleaseRoutine(Mathf.Clamp01(chargeFactor)));
    }

    private void Update()
    {
        if (_ring == null || _released) return;

        // Ring tightens from 1.4x → 1.0x of the slam radius as charge fills.
        float r = _baseRadius * Mathf.Lerp(1.4f, 1.0f, _charge);
        for (int i = 0; i < _ring.positionCount; i++)
        {
            float a = (i / (float)_ring.positionCount) * Mathf.PI * 2f;
            _ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
        }

        // HEARTBEAT THROB — the ring pulses in time with the hammer's
        // heartbeat (same 1.8→5 Hz ramp as the runner). It thumps harder and
        // faster as the charge fills, so the whole charge visibly pounds.
        float beatHz = Mathf.Lerp(1.8f, 5f, _charge);
        float beat = Mathf.Sin(Time.unscaledTime * beatHz * Mathf.PI * 2f);
        float thump = Mathf.Pow(Mathf.Clamp01(beat * 0.5f + 0.5f), 2.2f);

        // The whole ring breathes outward a little on each beat.
        float ringPulse = 1f + thump * Mathf.Lerp(0.04f, 0.13f, _charge);
        transform.localScale = new Vector3(ringPulse, ringPulse, 1f);

        // Brighten + thicken with charge, with the thump driving the alpha.
        // Kept understated so the ring hints at the charge rather than glaring.
        float alphaPulse = 1f + thump * Mathf.Lerp(0.1f, 0.3f, _charge);
        Color c = Color.Lerp(_data.hammerShockwaveColor, Color.white, _charge * 0.5f);
        c.a = Mathf.Clamp01(Mathf.Lerp(0.04f, 0.18f, _charge) * alphaPulse);
        _ring.startColor = _ring.endColor = c;
        _ring.startWidth = _ring.endWidth =
            Mathf.Lerp(0.03f, 0.06f, _charge) * (1f + thump * 0.2f);
    }

    // Motes that spiral inward toward the centre, faster as the charge fills.
    private IEnumerator SpawnMotes()
    {
        while (!_released)
        {
            // Spawn rate ramps up with charge.
            float interval = Mathf.Lerp(0.12f, 0.03f, _charge);
            SpawnOneMote();
            yield return new WaitForSeconds(interval);
        }
    }

    private void SpawnOneMote()
    {
        var go = new GameObject("ChargeMote");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = HammerSlamSystem.GetSoftDiscSprite();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = MoteOrder;
        Color c = Color.Lerp(_data.hammerShockwaveColor, Color.white, 0.4f);
        sr.color = c;

        Transform t = go.transform;
        _motes.Add(t);
        StartCoroutine(MoteRoutine(t, sr));
    }

    private IEnumerator MoteRoutine(Transform t, SpriteRenderer sr)
    {
        // Start out near the ring, spiral inward to the centre.
        float ang = Random.Range(0f, Mathf.PI * 2f);
        float startR = _baseRadius * Random.Range(0.9f, 1.3f);
        float life = Random.Range(0.35f, 0.6f);
        float size = _baseRadius * Random.Range(0.05f, 0.1f);
        float spiral = Random.Range(2.5f, 4.5f) * (Random.value < 0.5f ? -1f : 1f);
        Color baseCol = sr.color;

        float e = 0f;
        while (e < life && t != null)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            float r = Mathf.Lerp(startR, 0f, p * p);     // accelerate inward
            float a = ang + spiral * p;
            t.localPosition = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            t.localScale = Vector3.one * size * (1f - p * 0.5f);
            Color c = baseCol; c.a = baseCol.a * (1f - p); sr.color = c;
            yield return null;
        }
        if (t != null) { _motes.Remove(t); Destroy(t.gameObject); }
    }

    private IEnumerator ReleaseRoutine(float chargeFactor)
    {
        // A brief inward collapse + flash sized by how full the charge was.
        var flashGo = new GameObject("ChargeReleaseFlash");
        flashGo.transform.SetParent(transform, false);
        var flash = flashGo.AddComponent<SpriteRenderer>();
        flash.sprite = HammerSlamSystem.GetSoftDiscSprite();
        flash.sortingLayerName = SortLayer;
        flash.sortingOrder = MoteOrder + 1;
        Color hot = Color.Lerp(_data.hammerShockwaveColor, Color.white, 0.8f);
        flash.color = hot;

        float life = 0.18f;
        float peak = _baseRadius * Mathf.Lerp(0.5f, 1.1f, chargeFactor);
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            flashGo.transform.localScale = Vector3.one * Mathf.Lerp(peak, peak * 0.3f, p);
            Color c = hot; c.a = (1f - p) * Mathf.Lerp(0.4f, 0.95f, chargeFactor);
            flash.color = c;

            // Fade the ring out as the flash collapses.
            if (_ring != null)
            {
                Color rc = _ring.startColor; rc.a *= (1f - p);
                _ring.startColor = _ring.endColor = rc;
            }
            yield return null;
        }
        Destroy(gameObject);
    }
}

//  SLAM VFX — the dusty shockwave, cracks, debris, flash
public class HammerSlamVFX : MonoBehaviour
{
    // Sorting strategy — the player's YSortEntity sits around order ~1000.
    //   Cracks  : WELL BELOW the player  → look painted on the ground.
    //   Dust    : ABOVE the player       → billows in front, reads as volume.
    //   Debris  : ABOVE the player       → chunks fly toward camera.
    private const string SortLayer = "Default";
    // NOTE: ground marks must sit ABOVE *both* backgrounds — the tiled
    // "Background" object (sortingOrder -100) AND the legacy non-tiled center
    // rectangle from TowerDefenseMap (map.backgroundGameObject, ~ -1/0). They
    // still stay well below the player / cartoon grass (~1000) so they read as
    // painted on the ground.
    private const int CrackOrder = 50;    // painted on the ground, above both backgrounds
    private const int ScorchOrder = 40;   // darkened ground patch, just under the cracks
    private const int DustOrder = 5200;   // in front of player
    private const int DebrisOrder = 5400;   // in front of dust
    private const int FlashOrder = 5600;   // topmost burst

    private WeaponData _data;
    private float _radius;
    private Color _shock;
    private Color _crack;

    public void Play(WeaponData data, float chargeFactor = 0f)
    {
        _data = data;
        // The blast radius (and therefore the whole VFX) grows with charge,
        // matching the charge-scaled damage radius in HammerSlamRunner.
        float radiusMul = Mathf.Lerp(1f, Mathf.Max(1f, data.hammerChargeRadiusBonus),
                                     Mathf.Clamp01(chargeFactor));
        _radius = Mathf.Max(0.4f, data.hammerSlamRadius) * radiusMul;
        _shock = data.hammerShockwaveColor;
        _crack = data.hammerCrackColor;
        // A charged slam tints the shockwave hotter/brighter for extra punch.
        _shock = Color.Lerp(_shock, Color.white, 0.35f * Mathf.Clamp01(chargeFactor));
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        // Instant ground marks (behind player).
        BuildScorch();
        BuildCracks();

        // Front-of-player volume + burst.
        BuildDustShockwave();   // the rolling dusty ring
        BuildDustDisc();        // soft expanding ground-hugging cloud
        BuildDebris();          // arcing rock chunks
        BuildCoreFlash();       // sharp central pop

        // The component lives long enough for every spawned coroutine to
        // finish — debris (flight + bounce + rest + fade) is the longest-lived.
        yield return new WaitForSeconds(2.6f);
        Destroy(gameObject);
    }

    //  SCORCH: a dark patch stamped on the ground at impact 
    private void BuildScorch()
    {
        var go = new GameObject("Scorch");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = HammerSlamSystem.GetSoftDiscSprite();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = ScorchOrder;
        Color c = _crack; c.a = 0.16f;   // very faint — barely-there ground stain
        sr.color = c;
        go.transform.localScale = Vector3.one * (_radius * 1.2f);
        StartCoroutine(FadeSprite(sr, 1.6f, 0.4f));
    }

    //  CRACKS: jagged radial fissures, rendered BEHIND the player 
    private void BuildCracks()
    {
        int crackCount = 9;
        for (int i = 0; i < crackCount; i++)
        {
            float baseAngle = (i / (float)crackCount) * 360f + Random.Range(-16f, 16f);
            BuildOneCrack(baseAngle);
        }
    }

    private void BuildOneCrack(float angleDeg)
    {
        // A crack is a chain of 2-3 short segments at slightly varying angles,
        // giving a jagged "fault line" rather than a straight spoke.
        var crackRoot = new GameObject("Crack");
        crackRoot.transform.SetParent(transform, false);

        int segments = Random.Range(2, 4);
        float angle = angleDeg;
        Vector3 cursor = Vector3.zero;
        var renderers = new List<SpriteRenderer>();

        float totalLen = _radius * Random.Range(0.6f, 1.05f);
        float segLen = totalLen / segments;

        for (int s = 0; s < segments; s++)
        {
            angle += Random.Range(-26f, 26f);
            float thickness = Mathf.Lerp(0.13f, 0.04f, s / (float)segments) * Random.Range(0.8f, 1.2f);

            var go = new GameObject($"Seg{s}");
            go.transform.SetParent(crackRoot.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = HammerSlamSystem.GetPixelSprite();
            sr.sortingLayerName = SortLayer;
            sr.sortingOrder = CrackOrder;
            sr.color = _crack;

            Quaternion rot = Quaternion.Euler(0f, 0f, angle);
            Vector3 mid = cursor + rot * new Vector3(segLen * 0.5f, 0f, 0f);
            go.transform.localPosition = mid;
            go.transform.localRotation = rot;
            go.transform.localScale = new Vector3(segLen * 1.04f, thickness, 1f);

            renderers.Add(sr);
            cursor += rot * new Vector3(segLen, 0f, 0f);
        }

        StartCoroutine(CrackGrowIn(crackRoot.transform, renderers));
    }

    private IEnumerator CrackGrowIn(Transform root, List<SpriteRenderer> segs)
    {
        // Cracks snap outward fast, hold, then slowly fade.
        float grow = 0.12f;
        float e = 0f;
        while (e < grow)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / grow);
            root.localScale = new Vector3(t, 1f, 1f); // extend along the radius
            yield return null;
        }
        root.localScale = Vector3.one;

        yield return new WaitForSeconds(0.7f);

        float fade = 0.9f;
        e = 0f;
        var baseCols = new List<Color>();
        foreach (var s in segs) baseCols.Add(s != null ? s.color : Color.clear);
        while (e < fade)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / fade);
            for (int i = 0; i < segs.Count; i++)
            {
                if (segs[i] == null) continue;
                Color c = baseCols[i]; c.a = baseCols[i].a * (1f - t);
                segs[i].color = c;
            }
            yield return null;
        }
        if (root != null) Destroy(root.gameObject);
    }

    //  DUSTY SHOCKWAVE: a rolling ring of dust puffs travelling outward 

    private void BuildDustShockwave()
    {
        int puffs = Mathf.Max(8, _data.hammerDustCount);
        for (int i = 0; i < puffs; i++)
        {
            float ang = (i / (float)puffs) * Mathf.PI * 2f + Random.Range(-0.12f, 0.12f);
            StartCoroutine(ShockwavePuff(ang, i));
        }
    }

    private IEnumerator ShockwavePuff(float angle, int index)
    {
        var go = new GameObject("ShockPuff");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = HammerSlamSystem.GetSoftDiscSprite();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = DustOrder + (index % 5);

        // Dust is a desaturated, lightened mix of the warm impact tint.
        Color dust = Color.Lerp(_shock, new Color(0.82f, 0.77f, 0.68f), 0.55f);

        Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        float startR = _radius * 0.12f;
        float endR = _radius * Random.Range(0.95f, 1.22f);
        float life = Random.Range(0.42f, 0.62f);
        float startSize = _radius * Random.Range(0.18f, 0.28f);
        float endSize = _radius * Random.Range(0.5f, 0.78f);
        float spin = Random.Range(-120f, 120f);

        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / life);
            // Ease-out travel: fast launch, decelerating like real dust.
            float travel = 1f - (1f - t) * (1f - t);
            float r = Mathf.Lerp(startR, endR, travel);
            go.transform.localPosition = (Vector3)(dir * r);
            go.transform.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, t);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, spin * t);

            // Bright at birth, fade as it rolls out.
            float alpha = Mathf.Lerp(0.85f, 0f, t * t);
            Color c = dust; c.a = alpha;
            sr.color = c;
            yield return null;
        }
        Destroy(go);
    }

    //  DUST DISC: a soft low cloud that hugs the ground and expands once 
    private void BuildDustDisc()
    {
        var go = new GameObject("DustDisc");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = HammerSlamSystem.GetSoftDiscSprite();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = DustOrder - 1;
        StartCoroutine(DustDiscRoutine(go.transform, sr));
    }

    private IEnumerator DustDiscRoutine(Transform t, SpriteRenderer sr)
    {
        Color dust = Color.Lerp(_shock, new Color(0.78f, 0.73f, 0.65f), 0.6f);
        float life = 0.55f;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            float eased = 1f - (1f - p) * (1f - p);
            t.localScale = Vector3.one * Mathf.Lerp(_radius * 0.4f, _radius * 2.05f, eased);
            Color c = dust; c.a = Mathf.Lerp(0.5f, 0f, p);
            sr.color = c;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    //  DEBRIS: rock chunks that launch up, arc, land, bounce and settle 
    private void BuildDebris()
    {
        int chunks = Mathf.Max(4, _data.hammerDebrisCount);
        for (int i = 0; i < chunks; i++)
        {
            // Two classes of debris for a more natural scatter:
            //  - ~70% small grit/pebbles (tiny, fast, short arcs)
            //  - ~30% bigger chunks (chunkier, heavier, slower arcs)
            bool bigChunk = Random.value < 0.3f;
            StartCoroutine(DebrisChunk(bigChunk));
        }
    }

    private IEnumerator DebrisChunk(bool bigChunk)
    {
        var go = new GameObject("Debris");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        // Irregular jagged rock silhouette with baked shading — no longer a square.
        sr.sprite = HammerSlamSystem.GetRockChunkSprite();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = DebrisOrder;

        // Dark earth tone, lightly varied so chunks don't look identical. A few
        // carry a faint warm dust tint as if catching the impact light.
        Color earth = Color.Lerp(_crack, _shock, Random.Range(0f, 0.22f));
        earth = Color.Lerp(earth, earth * 1.25f, Random.Range(0f, 0.5f)); // tonal variation
        sr.color = earth;

        // Smaller than before, and non-uniform (slightly squashed/stretched) so
        // each chunk has its own irregular footprint rather than a clean shape.
        float baseSize = bigChunk
            ? _radius * Random.Range(0.07f, 0.11f)
            : _radius * Random.Range(0.025f, 0.055f);
        float aspectX = Random.Range(0.75f, 1.25f);
        float aspectY = Random.Range(0.75f, 1.25f);
        Vector3 restScale = new Vector3(baseSize * aspectX, baseSize * aspectY, 1f);
        go.transform.localScale = restScale;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        // Launch outward + up; gravity pulls it into an arc, then it lands.
        float ang = Random.Range(0f, Mathf.PI * 2f);
        Vector2 outward = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
        float launchDist = _radius * (bigChunk
            ? Random.Range(0.3f, 0.7f)
            : Random.Range(0.45f, 1.15f));
        float launchHeight = _radius * (bigChunk
            ? Random.Range(0.35f, 0.7f)
            : Random.Range(0.5f, 1.05f));
        float flightTime = Random.Range(0.42f, 0.7f) * (bigChunk ? 1.15f : 1f);
        float spin = Random.Range(-720f, 720f) * (bigChunk ? 0.5f : 1f);

        Vector3 start = Vector3.zero;
        float spinAccum = Random.Range(0f, 360f);

        //  FLIGHT: parabolic arc to the landing point 
        float e = 0f;
        while (e < flightTime)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / flightTime);

            float horiz = 1f - (1f - t) * (1f - t);          // ease-out spread
            Vector3 ground = start + (Vector3)(outward * launchDist * horiz);
            float height = launchHeight * 4f * t * (1f - t); // up-then-down parabola

            go.transform.localPosition = ground + Vector3.up * height;
            spinAccum += spin * Time.deltaTime;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, spinAccum);
            yield return null;
        }

        // Settle position: where the chunk actually lands.
        Vector3 landPos = start + (Vector3)(outward * launchDist);
        go.transform.localPosition = landPos;

        //  LANDING: a small squash, a tiny bounce, then rest 
        // Puff a bit of dust where it hits the ground.
        SpawnLandingDust(landPos, baseSize);

        // Squash on impact.
        float squashTime = 0.06f;
        e = 0f;
        while (e < squashTime)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / squashTime);
            go.transform.localScale = new Vector3(
                restScale.x * Mathf.Lerp(1f, 1.35f, t),
                restScale.y * Mathf.Lerp(1f, 0.6f, t),
                1f);
            yield return null;
        }
        // Small bounce hop + un-squash.
        float bounceHeight = launchHeight * (bigChunk ? 0.1f : 0.16f);
        float bounceTime = 0.14f;
        e = 0f;
        while (e < bounceTime)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / bounceTime);
            float hop = bounceHeight * 4f * t * (1f - t);
            go.transform.localPosition = landPos + Vector3.up * hop;
            float s = Mathf.Lerp(0f, 1f, t);
            go.transform.localScale = new Vector3(
                restScale.x * Mathf.Lerp(1.35f, 1f, s),
                restScale.y * Mathf.Lerp(0.6f, 1f, s),
                1f);
            spinAccum += spin * 0.25f * Time.deltaTime;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, spinAccum);
            yield return null;
        }
        go.transform.localPosition = landPos;
        go.transform.localScale = restScale;

        // ── REST then FADE: the chunk lies on the ground briefly, then fades ──
        yield return new WaitForSeconds(Random.Range(0.25f, 0.55f));

        float fade = 0.4f;
        e = 0f;
        Color baseCol = sr.color;
        while (e < fade)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / fade);
            Color c = baseCol; c.a = baseCol.a * (1f - t); sr.color = c;
            yield return null;
        }
        Destroy(go);
    }

    // A tiny soft dust puff where a debris chunk strikes the ground.
    private void SpawnLandingDust(Vector3 localPos, float chunkSize)
    {
        var go = new GameObject("DebrisDust");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = HammerSlamSystem.GetSoftDiscSprite();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = DebrisOrder - 1;
        Color dust = Color.Lerp(_shock, new Color(0.8f, 0.75f, 0.67f), 0.6f);
        dust.a = 0.5f;
        sr.color = dust;
        StartCoroutine(LandingDustRoutine(go.transform, sr, chunkSize));
    }

    private IEnumerator LandingDustRoutine(Transform t, SpriteRenderer sr, float chunkSize)
    {
        float life = 0.32f;
        float startSize = chunkSize * 1.2f;
        float endSize = chunkSize * 3.2f;
        Color baseCol = sr.color;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            t.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, p);
            Color c = baseCol; c.a = baseCol.a * (1f - p); sr.color = c;
            yield return null;
        }
        Destroy(t.gameObject);
    }


    //  CORE FLASH: a sharp bright pop at the point of impact 
    private void BuildCoreFlash()
    {
        var go = new GameObject("CoreFlash");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = HammerSlamSystem.GetSoftDiscSprite();
        sr.sortingLayerName = SortLayer;
        sr.sortingOrder = FlashOrder;
        StartCoroutine(CoreFlashRoutine(go.transform, sr));
    }

    private IEnumerator CoreFlashRoutine(Transform t, SpriteRenderer sr)
    {
        Color hot = Color.Lerp(_shock, Color.white, 0.7f);
        float life = 0.22f;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float p = Mathf.Clamp01(e / life);
            // Snap big instantly, shrink as it fades.
            t.localScale = Vector3.one * Mathf.Lerp(_radius * 1.15f, _radius * 0.5f, p);
            Color c = hot; c.a = Mathf.Lerp(0.95f, 0f, p);
            sr.color = c;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    //  SHARED FADE HELPER 
    private IEnumerator FadeSprite(SpriteRenderer sr, float life, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (sr == null) yield break;
        Color baseCol = sr.color;
        float e = 0f;
        while (e < life)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / life);
            Color c = baseCol; c.a = baseCol.a * (1f - t);
            sr.color = c;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }
}

