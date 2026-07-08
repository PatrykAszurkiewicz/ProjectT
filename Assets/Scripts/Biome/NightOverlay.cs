using UnityEngine;
using UnityEngine.InputSystem;

public class NightOverlay : MonoBehaviour
{
    //  Preset 
    public enum NightPreset
    {
        Dusk,        // Dim evening — things are visible, torch is a bonus
        Dark,        // Standard night — enemies barely visible without torch
        PitchBlack,  // Near-total darkness — only torch reveals anything
        Custom       // Full manual control
    }

    [Header("Darkness Preset")]
    [Tooltip("Quick preset that configures all darkness values. " +
             "Set to Custom to tweak individual values without them being overwritten.")]
    public NightPreset preset = NightPreset.Dark;

    //  Darkness 
    [Header("Darkness (auto-set by preset unless Custom)")]
    [Tooltip("Master darkness. 0 = fully transparent, 1 = pitch black. " +
             "At 1.0 with ambient 0 and glow 0, nothing is visible except the torch cone.")]
    [Range(0f, 1f)]
    public float darkness = 0.92f;

    [Tooltip("Base color of the night (dark blue-ish black)")]
    public Color nightColor = new Color(0.02f, 0.02f, 0.06f, 1f);

    [Tooltip("Ambient light — minimum visibility everywhere. " +
             "0 = truly pitch black outside torch. 0.08 = enemies faintly visible.")]
    [Range(0f, 0.3f)]
    public float ambientLight = 0.04f;

    //  Torch — Cone Shape 
    [Header("Torch — Cone Shape")]
    [Tooltip("Enable/disable the torch")]
    public bool torchEnabled = true;

    [Tooltip("Length of the torch cone in world units")]
    public float torchRange = 8f;

    [Tooltip("Half-angle of the cone in degrees (22 = 44° total spread)")]
    [Range(5f, 60f)]
    public float torchHalfAngle = 22f;

    [Tooltip("Softness of the cone edge (0 = hard, 1 = very soft)")]
    [Range(0f, 1f)]
    public float torchEdgeSoftness = 0.35f;

    //  Torch — Brightness 
    [Header("Torch — Brightness")]
    [Tooltip("How much the torch reveals. 1 = full reveal, 0.5 = dim torch, 0 = torch shows nothing.")]
    [Range(0f, 1f)]
    public float torchBrightness = 1.0f;

    //  Player Glow 
    [Header("Player Glow")]
    [Tooltip("Radius of the always-on glow around the player")]
    public float playerGlowRadius = 1.8f;

    [Tooltip("Strength of the player glow. 0 = no glow (pitch black around player), " +
             "0.6 = default warm glow, 1 = strong halo.")]
    [Range(0f, 1f)]
    public float playerGlowStrength = 0.4f;

    //  Torch — Warm Tint & Flicker 
    [Header("Torch — Warm Tint & Flicker")]
    [Tooltip("Warm color tint at the torch center")]
    public Color torchWarmTint = new Color(1.0f, 0.85f, 0.55f, 0.12f);

    [Tooltip("Flicker speed (0 = no flicker)")]
    public float flickerSpeed = 3.5f;

    [Tooltip("Flicker intensity (how much the range varies)")]
    [Range(0f, 0.15f)]
    public float flickerIntensity = 0.06f;

    //  Directional Darkness (Corruption biome) 
    [Header("Directional Darkness (Corruption biome)")]
    [Tooltip("When enabled, the radial player glow is replaced by a tiny feet bubble + " +
             "a wide dim front cone. Behind the player is pure darkness. " +
             "Used by the Corruption biome; leave off for Night biome and universal night mode.")]
    public bool directionalMode = false;

    [Tooltip("Half-angle of the wide front-awareness cone, in degrees. " +
             "80° gives roughly the human peripheral-vision arc.")]
    [Range(20f, 120f)]
    public float frontConeHalfAngle = 80f;

    [Tooltip("How far the front-awareness cone reaches (world units). " +
             "Should be smaller than torchRange so the torch still feels brighter.")]
    public float frontConeRange = 5f;

    [Tooltip("Brightness of the front cone. 0 = invisible (pure darkness), " +
             "1 = as bright as torch. 0.20-0.30 = enemies are vague shapes.")]
    [Range(0f, 1f)]
    public float frontConeDimming = 0.25f;

    [Tooltip("Radius of the tiny always-on glow at the player's feet. " +
             "Keeps the immediate area faintly visible so the player isn't standing in pure black.")]
    public float feetGlowRadius = 0.7f;

    [Tooltip("Strength of the feet glow. Keep low (0.2-0.4) — this is just a hint, " +
             "not real lighting.")]
    [Range(0f, 1f)]
    public float feetGlowStrength = 0.35f;


    [Header("Sorting")]
    public string sortingLayerName = "Default";
    public int sortingOrder = 6000;


    private GameObject overlayGO;
    private Material overlayMat;
    private Transform playerTransform;
    private NightPreset lastAppliedPreset = (NightPreset)(-1);

    // Co-op: the shader lights up to this many player torches/glows at once.
    // Keep in sync with the _PlayerPositions[N] / _PlayerFacings[N] array size
    // in NightOverlayShaderSource.ShaderCode.
    public const int MAX_NIGHT_PLAYERS = 4;

    // Reused each frame to avoid per-frame GC.
    private readonly Vector4[] playerPosArray = new Vector4[MAX_NIGHT_PLAYERS];
    private readonly Vector4[] playerFacingArray = new Vector4[MAX_NIGHT_PLAYERS];

    // Last-known facing direction — used in directional mode when the torch is
    // off or the cursor hasn't moved yet. Defaults to "right".
    private Vector2 lastFacing = Vector2.right;

    // Cached shader property IDs
    private static readonly int _PlayerCount = Shader.PropertyToID("_PlayerCount");
    private static readonly int _PlayerPositions = Shader.PropertyToID("_PlayerPositions");
    private static readonly int _PlayerFacings = Shader.PropertyToID("_PlayerFacings");
    private static readonly int _TorchEnabled = Shader.PropertyToID("_TorchEnabled");
    private static readonly int _Darkness = Shader.PropertyToID("_Darkness");
    private static readonly int _AmbientLight = Shader.PropertyToID("_AmbientLight");
    private static readonly int _NightColor = Shader.PropertyToID("_NightColor");
    private static readonly int _TorchRange = Shader.PropertyToID("_TorchRange");
    private static readonly int _TorchHalfAngle = Shader.PropertyToID("_TorchHalfAngle");
    private static readonly int _TorchEdgeSoftness = Shader.PropertyToID("_TorchEdgeSoftness");
    private static readonly int _PlayerGlowRadius = Shader.PropertyToID("_PlayerGlowRadius");
    private static readonly int _PlayerGlowStrength = Shader.PropertyToID("_PlayerGlowStrength");
    private static readonly int _TorchBrightness = Shader.PropertyToID("_TorchBrightness");
    private static readonly int _TorchWarmTint = Shader.PropertyToID("_TorchWarmTint");
    private static readonly int _FlickerOffset = Shader.PropertyToID("_FlickerOffset");
    private static readonly int _ExtraLightCountID = Shader.PropertyToID("_ExtraLightCount");
    private static readonly int _ExtraLightDataID = Shader.PropertyToID("_ExtraLightData");
    private static readonly int _ExtraLightColorsID = Shader.PropertyToID("_ExtraLightColors");

    // Directional darkness property IDs
    private static readonly int _DirectionalMode = Shader.PropertyToID("_DirectionalMode");
    private static readonly int _FrontConeHalfAngle = Shader.PropertyToID("_FrontConeHalfAngle");
    private static readonly int _FrontConeRange = Shader.PropertyToID("_FrontConeRange");
    private static readonly int _FrontConeDimming = Shader.PropertyToID("_FrontConeDimming");
    private static readonly int _FeetGlowRadius = Shader.PropertyToID("_FeetGlowRadius");
    private static readonly int _FeetGlowStrength = Shader.PropertyToID("_FeetGlowStrength");

    // ========================================================================
    // EXTRA POINT LIGHT SYSTEM
    // Other scripts call NightOverlay.RegisterLight / UnregisterLight to add
    // dynamic light sources that illuminate through the darkness identically
    // to the player torch — fire, lightning, lasers, gunge, etc.
    // ========================================================================

    public const int MAX_EXTRA_LIGHTS = 64;

    /// Singleton — the currently active NightOverlay instance.
    /// NightLight components use this to register without FindFirstObjectByType.
    public static NightOverlay Instance { get; private set; }

    /// Describes one dynamic point light in the night overlay.
    public class NightLightHandle
    {
        internal int id;
        public Vector2 position;
        public float radius;
        public float intensity;
        public Color color;
        public float warmTintStrength;
        internal bool alive;
    }

    private readonly System.Collections.Generic.List<NightLightHandle> extraLights =
        new System.Collections.Generic.List<NightLightHandle>();
    private int nextLightId = 0;

    // Shader data arrays (reused each frame to avoid GC)
    private readonly Vector4[] extraLightDataArray = new Vector4[MAX_EXTRA_LIGHTS];
    private readonly Vector4[] extraLightColorArray = new Vector4[MAX_EXTRA_LIGHTS];

    /// Register a new point light. Returns a handle — update handle.position etc. each frame.
    /// Call UnregisterLight when done.
    public static NightLightHandle RegisterLight(Vector2 position, float radius, float intensity,
                                                  Color color, float warmTintStrength = 0.3f)
    {
        if (Instance == null) return null;

        var handle = new NightLightHandle
        {
            id = Instance.nextLightId++,
            position = position,
            radius = radius,
            intensity = intensity,
            color = color,
            warmTintStrength = warmTintStrength,
            alive = true
        };
        Instance.extraLights.Add(handle);
        return handle;
    }

    /// Remove a previously registered light.
    public static void UnregisterLight(NightLightHandle handle)
    {
        if (handle == null) return;
        handle.alive = false;
        if (Instance != null)
            Instance.extraLights.Remove(handle);
    }

    /// Push all registered extra lights to the shader material.
    private void PushExtraLights()
    {
        if (overlayMat == null) return;

        // Cull dead/null entries
        extraLights.RemoveAll(h => h == null || !h.alive);

        int count = Mathf.Min(extraLights.Count, MAX_EXTRA_LIGHTS);
        for (int i = 0; i < count; i++)
        {
            var h = extraLights[i];
            extraLightDataArray[i] = new Vector4(h.position.x, h.position.y, h.radius, h.intensity);
            extraLightColorArray[i] = new Vector4(h.color.r, h.color.g, h.color.b, h.warmTintStrength);
        }
        // Zero out unused slots
        for (int i = count; i < MAX_EXTRA_LIGHTS; i++)
        {
            extraLightDataArray[i] = Vector4.zero;
            extraLightColorArray[i] = Vector4.zero;
        }

        overlayMat.SetFloat(_ExtraLightCountID, count);
        overlayMat.SetVectorArray(_ExtraLightDataID, extraLightDataArray);
        overlayMat.SetVectorArray(_ExtraLightColorsID, extraLightColorArray);
    }

    //  PRESETS


    public void ApplyPreset(NightPreset p)
    {
        preset = p;
        lastAppliedPreset = p;

        switch (p)
        {
            case NightPreset.Dusk:
                // Gentle evening — things are visible, torch is a bonus
                darkness = 0.65f;
                ambientLight = 0.18f;
                nightColor = new Color(0.04f, 0.04f, 0.10f, 1f);
                playerGlowRadius = 2.5f;
                playerGlowStrength = 0.7f;
                torchBrightness = 1.0f;
                torchRange = 10f;
                torchHalfAngle = 28f;
                torchEdgeSoftness = 0.45f;
                flickerIntensity = 0.03f;
                break;

            case NightPreset.Dark:
                // Proper night — enemies barely visible, torch crucial
                darkness = 0.92f;
                ambientLight = 0.04f;
                nightColor = new Color(0.02f, 0.02f, 0.06f, 1f);
                playerGlowRadius = 1.8f;
                playerGlowStrength = 0.35f;
                torchBrightness = 1.0f;
                torchRange = 8f;
                torchHalfAngle = 22f;
                torchEdgeSoftness = 0.35f;
                flickerIntensity = 0.06f;
                break;

            case NightPreset.PitchBlack:
                // Near-total darkness — ONLY the torch reveals anything.
                // Enemies are invisible until the beam sweeps over them.
                darkness = 0.99f;
                ambientLight = 0.0f;
                nightColor = new Color(0.01f, 0.01f, 0.02f, 1f);
                playerGlowRadius = 0.8f;
                playerGlowStrength = 0.08f;  // Tiny dim halo so player can find themselves
                torchBrightness = 0.85f;     // Torch itself is slightly dimmer for tension
                torchRange = 7f;
                torchHalfAngle = 18f;        // Narrower beam — more claustrophobic
                torchEdgeSoftness = 0.25f;
                flickerIntensity = 0.10f;    // More flicker — feels unreliable
                break;

            case NightPreset.Custom:
                break;
        }
    }

    //  GENERATION

    public void GenerateNight()
    {
        Cleanup();

        Instance = this;

        // Apply preset values (unless Custom)
        if (preset != NightPreset.Custom)
            ApplyPreset(preset);

        CreateOverlayQuad();
        FindPlayer();
    }

    private void CreateOverlayQuad()
    {
        overlayGO = new GameObject("NightOverlay_Quad");
        overlayGO.transform.SetParent(null);
        overlayGO.transform.position = Vector3.zero;

        float size = 200f;
        Mesh mesh = new Mesh();
        mesh.name = "NightQuad";
        mesh.vertices = new Vector3[]
        {
            new Vector3(-size, -size, 0),
            new Vector3(-size, size, 0),
            new Vector3(size, size, 0),
            new Vector3(size, -size, 0)
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(1, 0)
        };
        mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();

        MeshFilter mf = overlayGO.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = overlayGO.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Shader customShader = Shader.Find("Hidden/NightOverlay");
        if (customShader != null && customShader.isSupported)
        {
            overlayMat = new Material(customShader);
        }
        else
        {
            overlayMat = NightOverlayShaderSource.CreateMaterial();
        }

        mr.sharedMaterial = overlayMat;
        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = sortingOrder;

        PushAllProperties();
    }

    private void FindPlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
            return;
        }

        PlayerMovement pm = Object.FindFirstObjectByType<PlayerMovement>();
        if (pm != null)
        {
            playerTransform = pm.transform;
            return;
        }

        Debug.LogWarning("[NightOverlay] Could not find player. Torch will not track. " +
                         "Make sure your player has the 'Player' tag or a PlayerMovement component.");
    }

    //  MATERIAL SYNC

    private void PushAllProperties()
    {
        if (overlayMat == null) return;

        overlayMat.SetFloat(_Darkness, darkness);
        overlayMat.SetFloat(_AmbientLight, ambientLight);
        overlayMat.SetColor(_NightColor, nightColor);
        overlayMat.SetFloat(_TorchRange, torchRange);
        overlayMat.SetFloat(_TorchHalfAngle, torchHalfAngle * Mathf.Deg2Rad);
        overlayMat.SetFloat(_TorchEdgeSoftness, torchEdgeSoftness);
        overlayMat.SetFloat(_PlayerGlowRadius, playerGlowRadius);
        overlayMat.SetFloat(_PlayerGlowStrength, playerGlowStrength);
        overlayMat.SetFloat(_TorchBrightness, torchBrightness);
        overlayMat.SetColor(_TorchWarmTint, torchWarmTint);
        overlayMat.SetFloat(_TorchEnabled, torchEnabled ? 1f : 0f);

        // Directional darkness uniforms. Always pushed — when directionalMode
        // is false the shader's _DirectionalMode branch falls through to the
        // original radial-glow code path, so the rest of these values are
        // simply ignored. This keeps Night biome and universal night mode
        // bit-identical to the v2.2 behavior.
        overlayMat.SetFloat(_DirectionalMode, directionalMode ? 1f : 0f);
        overlayMat.SetFloat(_FrontConeHalfAngle, frontConeHalfAngle * Mathf.Deg2Rad);
        overlayMat.SetFloat(_FrontConeRange, frontConeRange);
        overlayMat.SetFloat(_FrontConeDimming, frontConeDimming);
        overlayMat.SetFloat(_FeetGlowRadius, feetGlowRadius);
        overlayMat.SetFloat(_FeetGlowStrength, feetGlowStrength);
    }



    void Update()
    {
        if (overlayMat == null) return;

        // Detect preset change in inspector at runtime
        if (preset != lastAppliedPreset && preset != NightPreset.Custom)
        {
            ApplyPreset(preset);
        }
        lastAppliedPreset = preset;

        // === Per-player positions + facings (co-op aware) ===
        // Build one entry per live player from PlayerAim.All. Each player's
        // torch cone and glow are anchored at that player's own position and
        // aimed by that player's own aim — so two players each get their own
        // correctly-aimed torch, instead of one shared cone driven by whichever
        // player last won PlayerAim.Instance.
        int count = 0;
        var aims = PlayerAim.All;
        if (aims != null && aims.Count > 0)
        {
            for (int idx = 0; idx < aims.Count && count < MAX_NIGHT_PLAYERS; idx++)
            {
                var a = aims[idx];
                if (a == null) continue;

                Vector3 pp = a.transform.position;
                Vector2 facing = a.Direction;                 // never zero by contract
                if (facing.sqrMagnitude < 0.0001f) facing = lastFacing;
                lastFacing = facing;

                playerPosArray[count] = new Vector4(pp.x, pp.y, 0, 0);
                playerFacingArray[count] = new Vector4(facing.x, facing.y, 0, 0);
                count++;
            }
        }

        // Legacy single-player fallback: no PlayerAim in the scene. Resolve the
        // player by tag / PlayerMovement and aim with the mouse, exactly as
        // before the co-op change.
        if (count == 0)
        {
            if (playerTransform == null) FindPlayer();
            if (playerTransform == null) return;

            Vector3 pp = playerTransform.position;
            Vector2 facing = lastFacing;
            if (Mouse.current != null && Camera.main != null)
            {
                Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
                mouseWorld.z = 0;
                Vector2 toCursor = (Vector2)(mouseWorld - pp);
                if (toCursor.sqrMagnitude > 0.0001f)
                {
                    facing = toCursor.normalized;
                    lastFacing = facing;
                }
            }

            playerPosArray[0] = new Vector4(pp.x, pp.y, 0, 0);
            playerFacingArray[0] = new Vector4(facing.x, facing.y, 0, 0);
            count = 1;
        }

        // Zero unused slots (the shader honors _PlayerCount, but keep them clean).
        for (int z = count; z < MAX_NIGHT_PLAYERS; z++)
        {
            playerPosArray[z] = Vector4.zero;
            playerFacingArray[z] = Vector4.zero;
        }

        overlayMat.SetFloat(_PlayerCount, count);
        overlayMat.SetVectorArray(_PlayerPositions, playerPosArray);
        overlayMat.SetVectorArray(_PlayerFacings, playerFacingArray);

        // Flicker
        float flicker = 0f;
        if (flickerSpeed > 0 && flickerIntensity > 0)
        {
            flicker = Mathf.PerlinNoise(Time.time * flickerSpeed, 0.5f) * 2f - 1f;
            flicker *= flickerIntensity;
        }
        overlayMat.SetFloat(_FlickerOffset, flicker);

        // Push all properties every frame for live inspector tweaking
        PushAllProperties();

        // Push dynamic light sources (fire, lightning, laser, gunge, etc.)
        PushExtraLights();

        // Keep overlay centered on camera
        if (Camera.main != null)
        {
            Vector3 camPos = Camera.main.transform.position;
            overlayGO.transform.position = new Vector3(camPos.x, camPos.y, 0);
        }
    }

    //  PUBLIC API

    /// <summary>Switch to a named preset at runtime.</summary>
    public void SetPreset(NightPreset p)
    {
        ApplyPreset(p);
    }

    /// <summary>Set master darkness (0–1). Automatically switches to Custom preset.</summary>
    public void SetDarkness(float value)
    {
        preset = NightPreset.Custom;
        darkness = Mathf.Clamp01(value);
    }

    /// <summary>Set ambient light (0–0.3). Automatically switches to Custom preset.</summary>
    public void SetAmbientLight(float value)
    {
        preset = NightPreset.Custom;
        ambientLight = Mathf.Clamp(value, 0f, 0.3f);
    }

    /// <summary>Set player glow strength (0–1). Automatically switches to Custom preset.</summary>
    public void SetPlayerGlowStrength(float value)
    {
        preset = NightPreset.Custom;
        playerGlowStrength = Mathf.Clamp01(value);
    }

    /// <summary>Set torch brightness (0–1). Automatically switches to Custom preset.</summary>
    public void SetTorchBrightness(float value)
    {
        preset = NightPreset.Custom;
        torchBrightness = Mathf.Clamp01(value);
    }

    /// <summary>Toggle torch on/off.</summary>
    public void SetTorchEnabled(bool enabled)
    {
        torchEnabled = enabled;
    }

    public void ToggleTorch()
    {
        torchEnabled = !torchEnabled;
    }

    public bool IsTorchEnabled()
    {
        return torchEnabled;
    }

    /// <summary>
    /// Enable/disable the directional-darkness mode used by the Corruption biome.
    /// When off, the player glow is radial (Night biome behavior).
    /// When on, the radial glow is replaced by a tiny feet bubble + wide dim front cone.
    /// </summary>
    public void SetDirectionalMode(bool enabled)
    {
        directionalMode = enabled;
    }

    //  CLEANUP

    private void Cleanup()
    {
        if (overlayGO != null)
        {
            MeshFilter mf = overlayGO.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                if (Application.isPlaying) Destroy(mf.sharedMesh);
                else DestroyImmediate(mf.sharedMesh);
            }

            if (overlayMat != null)
            {
                if (Application.isPlaying) Destroy(overlayMat);
                else DestroyImmediate(overlayMat);
            }

            if (Application.isPlaying) Destroy(overlayGO);
            else DestroyImmediate(overlayGO);
        }
    }

    void OnDestroy()
    {
        // Mark all handles dead so NightLight components know to re-register
        foreach (var h in extraLights) h.alive = false;
        extraLights.Clear();

        if (Instance == this)
            Instance = null;

        Cleanup();
    }
}

