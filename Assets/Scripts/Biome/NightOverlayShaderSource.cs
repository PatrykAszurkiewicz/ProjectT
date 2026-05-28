using UnityEngine;


public static class NightOverlayShaderSource
{
    // VERSION TAG
    // EnsureShaderAsset compares this against a marker file to know when to re-write.
    public const string ShaderVersion = "2.3_directional";

    // The full shader source code — kept in sync with NightOverlayShader.shader
    public static readonly string ShaderCode = @"
Shader ""Hidden/NightOverlay""
{
    Properties
    {
        _Darkness (""Darkness"", Range(0,1)) = 0.88
        _AmbientLight (""Ambient Light"", Range(0,0.3)) = 0.08
        _NightColor (""Night Color"", Color) = (0.02, 0.02, 0.06, 1)
        _PlayerPos (""Player Position"", Vector) = (0,0,0,0)
        _TorchDir (""Torch Direction"", Vector) = (1,0,0,0)
        _TorchEnabled (""Torch Enabled"", Float) = 1
        _TorchRange (""Torch Range"", Float) = 8
        _TorchHalfAngle (""Torch Half Angle Rad"", Float) = 0.384
        _TorchEdgeSoftness (""Edge Softness"", Range(0,1)) = 0.35
        _PlayerGlowRadius (""Player Glow Radius"", Float) = 1.8
        _PlayerGlowStrength (""Player Glow Strength"", Range(0,1)) = 0.6
        _TorchBrightness (""Torch Brightness"", Range(0,1)) = 1.0
        _TorchWarmTint (""Warm Tint"", Color) = (1, 0.85, 0.55, 0.12)
        _FlickerOffset (""Flicker"", Float) = 0
        _ExtraLightCount (""Extra Light Count"", Float) = 0

        // --- Directional darkness mode (Corruption biome) ---
        // When _DirectionalMode > 0.5, the radial player glow is replaced by:
        //   * a tiny feet bubble (always-on minimal visibility)
        //   * a wide, dim front cone (peripheral awareness in the direction the
        //     player is facing)
        // Behind the player there is no glow at all — pure darkness.
        _DirectionalMode (""Directional Mode"", Float) = 0
        _PlayerFacing (""Player Facing"", Vector) = (1,0,0,0)
        _FrontConeHalfAngle (""Front Cone Half Angle Rad"", Float) = 1.4
        _FrontConeRange (""Front Cone Range"", Float) = 5.0
        _FrontConeDimming (""Front Cone Dimming"", Range(0,1)) = 0.25
        _FeetGlowRadius (""Feet Glow Radius"", Float) = 0.7
        _FeetGlowStrength (""Feet Glow Strength"", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags { ""Queue""=""Overlay+100"" ""RenderType""=""Transparent"" ""IgnoreProjector""=""True"" }
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""

            float _Darkness;
            float _AmbientLight;
            float4 _NightColor;
            float4 _PlayerPos;
            float4 _TorchDir;
            float _TorchEnabled;
            float _TorchRange;
            float _TorchHalfAngle;
            float _TorchEdgeSoftness;
            float _PlayerGlowRadius;
            float _PlayerGlowStrength;
            float _TorchBrightness;
            float4 _TorchWarmTint;
            float _FlickerOffset;

            // Directional darkness uniforms
            float _DirectionalMode;
            float4 _PlayerFacing;
            float _FrontConeHalfAngle;
            float _FrontConeRange;
            float _FrontConeDimming;
            float _FeetGlowRadius;
            float _FeetGlowStrength;

            // Extra point lights — up to 64 dynamic light sources
            // Each entry: (x, y, radius, intensity)
            float _ExtraLightCount;
            float4 _ExtraLightData[64];
            // Each entry: (r, g, b, warmTintStrength)
            float4 _ExtraLightColors[64];

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 worldPos : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldPos = wp.xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 toFrag = i.worldPos - _PlayerPos.xy;
                float dist = length(toFrag);
                float2 dirNorm = (dist > 0.001) ? (toFrag / dist) : float2(0, 1);

                // === Player glow ===
                // Two modes — selected by _DirectionalMode uniform.
                // Default (0) = original radial glow, used by Night biome
                //               and universal night mode. Unchanged from v2.2.
                // Directional (1) = tiny feet bubble + wide front cone.
                //                   Used by the Corruption biome.
                float glowLight = 0.0;

                if (_DirectionalMode < 0.5)
                {
                    // --- Radial player glow (unchanged) ---
                    float glowFalloff = saturate(dist / max(0.01, _PlayerGlowRadius));
                    glowFalloff = glowFalloff * glowFalloff;
                    glowLight = (1.0 - glowFalloff) * _PlayerGlowStrength;
                }
                else
                {
                    // --- Directional player glow ---
                    // 1) Tiny feet bubble — keeps the immediate area around the
                    //    player faintly visible so they're never standing in pure
                    //    black. Uses the same quadratic falloff as radial mode.
                    float feetFalloff = saturate(dist / max(0.01, _FeetGlowRadius));
                    feetFalloff = feetFalloff * feetFalloff;
                    float feetLight = (1.0 - feetFalloff) * _FeetGlowStrength;

                    // 2) Wide, dim front cone — peripheral awareness in front of
                    //    the player. Same cone math as the torch, but wider, dimmer,
                    //    and tied to _PlayerFacing rather than _TorchDir.
                    float frontCos = dot(dirNorm, _PlayerFacing.xy);
                    frontCos = clamp(frontCos, -1.0, 1.0);
                    float frontFragAngle = acos(frontCos);

                    // Soft falloff at the edge of the front cone so it doesn't
                    // look like a flashlight ring.
                    float frontInner = _FrontConeHalfAngle * 0.55;
                    float frontOuter = _FrontConeHalfAngle;
                    float frontAngleFactor = 1.0 - saturate(
                        (frontFragAngle - frontInner) /
                        max(0.001, frontOuter - frontInner));

                    float frontDistNorm = dist / max(0.001, _FrontConeRange);
                    float frontDistFactor = saturate(1.0 - frontDistNorm);
                    frontDistFactor = frontDistFactor * frontDistFactor;

                    float frontLight = frontAngleFactor * frontDistFactor * _FrontConeDimming;

                    // Combine — take the max so the feet bubble doesn't get
                    // clobbered by the dim front cone right at the player.
                    glowLight = max(feetLight, frontLight);
                }

                // === Torch cone ===
                float coneLight = 0.0;
                float warmAmount = 0.0;

                if (_TorchEnabled > 0.5)
                {
                    float effectiveRange = _TorchRange * (1.0 + _FlickerOffset);

                    float cosAngle = dot(dirNorm, _TorchDir.xy);
                    cosAngle = clamp(cosAngle, -1.0, 1.0);
                    float fragAngle = acos(cosAngle);

                    float innerAngle = _TorchHalfAngle * (1.0 - _TorchEdgeSoftness);
                    float outerAngle = _TorchHalfAngle * (1.0 + _TorchEdgeSoftness * 0.5);
                    float angleFactor = 1.0 - saturate((fragAngle - innerAngle) / max(0.001, outerAngle - innerAngle));

                    float distNorm = dist / max(0.001, effectiveRange);
                    float distFactor = saturate(1.0 - distNorm);
                    distFactor = distFactor * distFactor;

                    float nearBoost = saturate(1.0 - dist / max(0.01, _PlayerGlowRadius * 2.0));
                    angleFactor = saturate(angleFactor + nearBoost * 0.3 * _PlayerGlowStrength);

                    coneLight = angleFactor * distFactor * _TorchBrightness;
                    warmAmount = coneLight * _TorchWarmTint.a;
                }

                // === Extra point lights ===
                // Each light works identically to player glow: radial quadratic falloff,
                // contribution adds directly into totalLight so it punches through darkness.
                float extraLight = 0.0;
                float3 extraWarmAccum = float3(0, 0, 0);
                float extraWarmWeight = 0.0;
                int lightCount = (int)_ExtraLightCount;

                for (int li = 0; li < lightCount && li < 64; li++)
                {
                    float4 ld = _ExtraLightData[li];
                    float2 toLightFrag = i.worldPos - ld.xy;
                    float lightDist = length(toLightFrag);
                    float lightRadius = max(0.01, ld.z);

                    // Smooth cosine falloff — much softer edges than quadratic,
                    // overlapping lights blend seamlessly without visible circles
                    float lf = saturate(lightDist / lightRadius);
                    float contribution = (0.5 + 0.5 * cos(lf * 3.14159)) * ld.w;

                    extraLight += contribution;

                    // Accumulate color tint weighted by contribution
                    float4 lc = _ExtraLightColors[li];
                    float tintWeight = contribution * lc.a;
                    extraWarmAccum += lc.rgb * tintWeight;
                    extraWarmWeight += tintWeight;
                }

                // === Combine ===
                float totalLight = saturate(glowLight + coneLight + extraLight + _AmbientLight);
                float alpha = _Darkness * (1.0 - totalLight);

                float3 color = _NightColor.rgb;
                color = lerp(color, _TorchWarmTint.rgb, saturate(warmAmount * 0.35));

                // Blend extra light color tint
                if (extraWarmWeight > 0.001)
                {
                    float3 avgTint = extraWarmAccum / extraWarmWeight;
                    color = lerp(color, avgTint, saturate(extraWarmWeight * 0.45));
                }

                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
";


    /// Creates the material. Tries the real shader first

    public static Material CreateMaterial()
    {
        Shader shader = Shader.Find("Hidden/NightOverlay");
        if (shader != null && shader.isSupported)
        {
            return new Material(shader);
        }

#if UNITY_EDITOR
        ForceWriteShaderAsset();

        shader = Shader.Find("Hidden/NightOverlay");
        if (shader != null && shader.isSupported)
        {
            return new Material(shader);
        }
#endif

        Debug.LogWarning("[NightOverlay] Custom shader not available. Using fallback. " +
                         "For best results, add NightOverlayShader.shader to Assets/Shaders/. " +
                         "You can copy it from NightOverlayShaderSource.ShaderCode.");

        shader = Shader.Find("Sprites/Default");
        Material mat = new Material(shader);
        mat.color = new Color(0.02f, 0.02f, 0.06f, 0.85f);
        return mat;
    }

    /// Writes the shader file unconditionally.
    private static void ForceWriteShaderAsset()
    {
#if UNITY_EDITOR
        string path = "Assets/Shaders/NightOverlayShader.shader";
        string dir = System.IO.Path.GetDirectoryName(path);
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);

        System.IO.File.WriteAllText(path, ShaderCode);
        UnityEditor.AssetDatabase.Refresh();
        //Debug.Log("[NightOverlayShaderSource] Shader force-written to " + path);
#endif
    }


    /// Call from an Editor script or [InitializeOnLoad] to ensure the shader is up-to-date. Uses a version marker file — only re-writes when version changes.

    public static void EnsureShaderAsset()
    {
#if UNITY_EDITOR
        string shaderPath = "Assets/Shaders/NightOverlayShader.shader";
        string versionPath = "Assets/Shaders/NightOverlayShader.version";

        bool needsWrite = false;

        if (!System.IO.File.Exists(shaderPath))
        {
            needsWrite = true;
        }
        else if (!System.IO.File.Exists(versionPath))
        {
            needsWrite = true;
        }
        else
        {
            string diskVersion = System.IO.File.ReadAllText(versionPath).Trim();
            if (diskVersion != ShaderVersion)
                needsWrite = true;
        }

        if (needsWrite)
        {
            string dir = System.IO.Path.GetDirectoryName(shaderPath);
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            System.IO.File.WriteAllText(shaderPath, ShaderCode);
            System.IO.File.WriteAllText(versionPath, ShaderVersion);
            UnityEditor.AssetDatabase.Refresh();
            //Debug.Log($"[NightOverlayShaderSource] Shader updated to version {ShaderVersion}");
        }
#endif
    }
}
