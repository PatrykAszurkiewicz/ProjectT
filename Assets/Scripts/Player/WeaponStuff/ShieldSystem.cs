using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// SHIELD SYSTEM
// Directional blocking + timed parry for the Shield tool if right-click is pressed within PARRY_WINDOW seconds of an incoming
// Integration points:
//   - Weapon.cs creates / destroys this system when shield tool is equipped.
//   - PlayerAttack.cs calls RaiseShield() / LowerShield().
//   - EnemyController.ApplyDamageToTarget() calls TryBlockOrParry().


public class ShieldSystem
{
    //  Tuning 
    private const float BLOCK_ARC_DEGREES = 120f;   // total arc width
    private const float PARRY_WINDOW = 0.2f;        // seconds after raise
    private const float PARRY_STUN_NORMAL = 3f;     // seconds
    private const float PARRY_STUN_BOSS = 2f;       // seconds
    private const float PARRY_DAMAGE_BONUS = 0.30f;  // +30%

    // Visual arc
    private const float ARC_RADIUS = 1.0f;
    private const int ARC_SEGMENTS = 16;
    private const float ARC_WIDTH = 0.08f;
    private const float VISUAL_ARC_DEGREES = BLOCK_ARC_DEGREES * 0.70f; // visual is shorter than hitbox

    //  State 
    private readonly Weapon weapon;
    private readonly WeaponData shieldData;

    private bool isRaised = false;
    private float raiseTime = -999f;

    // Track whether the shield is actively blocking (held down).

    private float lastLowerTime = -999f;  // when shield was last lowered

    // Grace period after releasing right-click during which a parry can still trigger

    private const float QUICK_PRESS_GRACE = 0.6f;



    // Visual objects
    private GameObject arcObject;
    private LineRenderer arcLine;
    private Material arcMaterial;

    // Parry VFX pool (reuse across parries)
    private static Sprite _shieldIconSprite;

    // Reference to player transform (cached)
    private Transform playerTransform;

    //  Construction / Cleanup 

    public ShieldSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.shieldData = data;

        // Cache player transform
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            playerTransform = player.transform;

        CreateArcVisual();
        SetArcVisible(false);
    }

    public void Cleanup()
    {
        if (arcObject != null) Object.Destroy(arcObject);
        arcObject = null;
        arcLine = null;
    }

    //  Public API 

    public bool IsRaised => isRaised;

    /// Expose the arc LineRenderer for visual feedback (ShieldFeedback uses this).
    public LineRenderer ArcLineRenderer => arcLine;

    public void RaiseShield()
    {
        if (isRaised) return;
        isRaised = true;
        raiseTime = Time.time;
        SetArcVisible(true);
    }

    public void LowerShield()
    {
        if (!isRaised) return;
        isRaised = false;
        lastLowerTime = Time.time;
        SetArcVisible(false);
    }


    /// Called every frame from Weapon.Update().
    /// Updates arc visual position and rotation.

    public void Update()
    {
        if (!isRaised || playerTransform == null) return;

        if (arcLine != null)
            UpdateArcTransform();
    }


    // Called by EnemyController (or any damage source) BEFORE applying damage.

    public bool TryBlockOrParry(GameObject attackerGO)
    {
        if (playerTransform == null || attackerGO == null)
            return false;

        // Shield must be either currently raised OR recently released (quick-press grace)
        bool currentlyRaised = isRaised;
        bool recentlyReleased = !isRaised && (Time.time - lastLowerTime) <= QUICK_PRESS_GRACE;

        if (!currentlyRaised && !recentlyReleased)
        {
            return false;
        }

        // Check if attack comes from within the shield arc.

        Vector2 cursorDir = GetCursorDirection();
        Vector2 attackDir = ((Vector2)attackerGO.transform.position - (Vector2)playerTransform.position).normalized;

        float angle = Vector2.Angle(cursorDir, attackDir);
        if (angle > BLOCK_ARC_DEGREES * 0.5f)
        {
            //Debug.Log($"[SHIELD] MISS — outside arc. angle={angle:F1}° (need <{BLOCK_ARC_DEGREES * 0.5f}°) " +
            //          $"cursor toward {cursorDir}, enemy at {attackDir}");
            return false;
        }

        // Parry check: only a fresh right-click PRESS during the parry window counts.
        // Holding the shield from before the window is just a block, not a parry.
        bool isParry = false;
        var ec = attackerGO.GetComponent<EnemyController>();
        if (ec != null)
        {
            isParry = ec.IsInParryWindow(raiseTime);

            //Debug.Log($"[PARRY EVAL] {attackerGO.name}: raiseInWindow={isParry} " +
            //          $"=> {(isParry ? "PARRY!" : "BLOCK")}");
        }
        else
        {
            // No EnemyController (boss projectile, etc.) — fallback
            isParry = (Time.time - raiseTime) <= PARRY_WINDOW;
        }

        // If the shield was already lowered (quick press), only a parry counts — not a block
        if (recentlyReleased && !isParry)
        {
            //Debug.Log($"[SHIELD] Quick-press but not in parry window — no block (shield is down)");
            return false;
        }

        //Debug.Log($"[SHIELD] {(isParry ? "PARRY!" : "BLOCK")} from {attackerGO.name} " +
        //          $"angle={angle:F1}° raised={currentlyRaised} recentRelease={recentlyReleased} " +
        //          $"raiseAge={Time.time - raiseTime:F3}s");

        if (isParry)
        {
            ApplyParry(attackerGO);
            SpawnParryVFX();

            // ── Visual + audio feedback (parry) ──
            ShieldFeedback.OnParry(playerTransform, attackerGO.transform.position);
        }
        else
        {
            // ── Visual + audio feedback (block) ──
            ShieldFeedback.OnBlock(playerTransform, attackerGO.transform.position, arcLine);
        }

        return true;
    }

    //  Parry Logic 

    private void ApplyParry(GameObject attackerGO)
    {
        if (attackerGO == null) return;

        bool isBoss = attackerGO.GetComponent<BaseBossStats>() != null;
        float stunDuration = isBoss ? PARRY_STUN_BOSS : PARRY_STUN_NORMAL;

        // Add or refresh ParryStunEffect
        var existing = attackerGO.GetComponent<ParryStunEffect>();
        if (existing != null)
        {
            existing.Refresh(stunDuration, PARRY_DAMAGE_BONUS);
        }
        else
        {
            var effect = attackerGO.AddComponent<ParryStunEffect>();
            effect.Initialize(stunDuration, PARRY_DAMAGE_BONUS);
        }
    }

    //  Cursor Direction 

    private Vector2 GetCursorDirection()
    {
        if (playerTransform == null) return Vector2.right;

        Vector2 mouseScreen = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(mouseScreen);
        mouseWorld.z = 0f;

        Vector2 dir = ((Vector2)mouseWorld - (Vector2)playerTransform.position).normalized;
        if (dir.sqrMagnitude < 0.001f) dir = Vector2.right;
        return dir;
    }

    //  Visual Arc 

    private void CreateArcVisual()
    {
        arcObject = new GameObject("ShieldArc");

        arcLine = arcObject.AddComponent<LineRenderer>();
        arcMaterial = new Material(Shader.Find("Sprites/Default"));
        arcLine.material = arcMaterial;
        arcLine.startWidth = ARC_WIDTH;
        arcLine.endWidth = ARC_WIDTH;
        arcLine.positionCount = ARC_SEGMENTS + 1;
        arcLine.useWorldSpace = true;
        arcLine.loop = false;
        arcLine.sortingOrder = 9000; // Above most things, below cursor

        // Semi-transparent blue-white
        Color arcColor = new Color(0.6f, 0.8f, 1f, 0.45f);
        arcLine.startColor = arcColor;
        arcLine.endColor = arcColor;
    }

    private void SetArcVisible(bool visible)
    {
        if (arcLine != null)
            arcLine.enabled = visible;
    }

    private void UpdateArcTransform()
    {
        if (arcLine == null || playerTransform == null) return;

        Vector2 cursorDir = GetCursorDirection();
        float centerAngle = Mathf.Atan2(cursorDir.y, cursorDir.x) * Mathf.Rad2Deg;
        float halfArc = VISUAL_ARC_DEGREES * 0.5f;

        Vector3 center = playerTransform.position;

        for (int i = 0; i <= ARC_SEGMENTS; i++)
        {
            float t = (float)i / ARC_SEGMENTS;
            float angle = (centerAngle - halfArc + t * VISUAL_ARC_DEGREES) * Mathf.Deg2Rad;
            Vector3 pos = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * ARC_RADIUS;
            arcLine.SetPosition(i, pos);
        }

        // Pulse alpha slightly for visual interest
        float pulse = 0.35f + Mathf.PingPong(Time.time * 2f, 0.2f);
        Color c = arcLine.startColor;
        c.a = pulse;
        arcLine.startColor = c;
        arcLine.endColor = c;
    }

    //  Parry VFX 

    private void SpawnParryVFX()
    {
        if (playerTransform == null) return;

        Vector2 cursorDir = GetCursorDirection();
        Vector3 vfxPos = playerTransform.position + (Vector3)(cursorDir * 1.2f);

        // Create a host object that self-destructs
        GameObject host = new GameObject("ParryVFX");
        host.transform.position = vfxPos;
        var vfx = host.AddComponent<ParryVFXHost>();
        vfx.Play();
    }

    //  Static: Procedural Shield Icon Sprite 

    public static Sprite GetShieldIconSprite()
    {
        if (_shieldIconSprite != null) return _shieldIconSprite;

        // 32x32 procedural shield shape
        const int S = 32;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color[] px = new Color[S * S];

        Vector2 center = new Vector2(S * 0.5f, S * 0.5f);

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                // Shield shape: rounded top, pointed bottom
                float nx = (x - center.x) / (S * 0.5f);  // -1 to 1
                float ny = (y - center.y) / (S * 0.5f);

                // Top half: ellipse; bottom half: triangle taper
                float halfWidth;
                if (ny >= 0f)
                    halfWidth = Mathf.Sqrt(Mathf.Max(0f, 1f - ny * ny)) * 0.85f;
                else
                    halfWidth = 0.85f * (1f + ny * 0.9f); // tapers to point

                bool inside = Mathf.Abs(nx) < halfWidth && ny > -0.95f;

                // Border: slightly smaller inner region
                float borderWidth = 0.15f;
                float innerHalfWidth = halfWidth - borderWidth;
                float innerBottom = -0.95f + borderWidth;
                bool innerInside = Mathf.Abs(nx) < innerHalfWidth && ny > innerBottom && ny < (1f - borderWidth);

                // Cross/emblem in center
                bool crossH = Mathf.Abs(ny - 0.05f) < 0.08f && Mathf.Abs(nx) < 0.25f;
                bool crossV = Mathf.Abs(nx) < 0.08f && ny > -0.25f && ny < 0.35f;
                bool emblem = crossH || crossV;

                if (inside)
                {
                    if (!innerInside || emblem)
                        px[y * S + x] = Color.white;          // border or emblem
                    else
                        px[y * S + x] = new Color(1f, 1f, 1f, 0.5f); // fill
                }
                else
                {
                    px[y * S + x] = Color.clear;
                }
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        _shieldIconSprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, 32f);
        return _shieldIconSprite;
    }
}


// PARRY VFX HOST
// Self-contained MonoBehaviour that plays the parry burst effect and destroys
// itself when done. Fully procedural — no prefabs or assets needed.

public class ParryVFXHost : MonoBehaviour
{
    private float elapsed = 0f;
    private const float DURATION = 0.6f;

    private SpriteRenderer iconRenderer;
    private List<SpriteRenderer> burstParticles = new List<SpriteRenderer>();

    public void Play()
    {
        // Central shield icon
        Sprite shieldSprite = ShieldSystem.GetShieldIconSprite();

        GameObject iconGO = new GameObject("ParryIcon");
        iconGO.transform.SetParent(transform, false);
        iconRenderer = iconGO.AddComponent<SpriteRenderer>();
        iconRenderer.sprite = shieldSprite;
        iconRenderer.sortingOrder = 9500;
        iconRenderer.color = new Color(0.8f, 0.9f, 1f, 1f);
        iconGO.transform.localScale = Vector3.zero;

        // Burst particles — small bright dots radiating outward
        int particleCount = 8;
        for (int i = 0; i < particleCount; i++)
        {
            GameObject pGO = new GameObject($"Burst_{i}");
            pGO.transform.SetParent(transform, false);
            pGO.transform.localPosition = Vector3.zero;

            SpriteRenderer psr = pGO.AddComponent<SpriteRenderer>();
            psr.sprite = GetBurstDotSprite();
            psr.sortingOrder = 9501;
            psr.color = new Color(0.7f, 0.85f, 1f, 1f);
            pGO.transform.localScale = Vector3.one * 0.15f;

            burstParticles.Add(psr);
        }
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / DURATION);

        // Icon: scale up then fade
        if (iconRenderer != null)
        {
            float scaleT = Mathf.Clamp01(elapsed / 0.15f);
            float scale = Mathf.Lerp(0f, 1.2f, EaseOutBack(scaleT));
            // Shrink back after peak
            if (t > 0.4f)
                scale *= Mathf.Lerp(1f, 0f, (t - 0.4f) / 0.6f);

            iconRenderer.transform.localScale = Vector3.one * scale;

            Color c = iconRenderer.color;
            c.a = 1f - t * t;
            iconRenderer.color = c;
        }

        // Burst particles: radiate outward
        int count = burstParticles.Count;
        for (int i = 0; i < count; i++)
        {
            if (burstParticles[i] == null) continue;

            float angle = (360f / count) * i * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(0f, 1.5f, Mathf.Sqrt(t));
            Vector3 pos = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            burstParticles[i].transform.localPosition = pos;

            float particleScale = Mathf.Lerp(0.2f, 0.05f, t);
            burstParticles[i].transform.localScale = Vector3.one * particleScale;

            Color pc = burstParticles[i].color;
            pc.a = 1f - t;
            burstParticles[i].color = pc;
        }

        if (elapsed >= DURATION)
            Destroy(gameObject);
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    private static Sprite _burstDot;
    private static Sprite GetBurstDotSprite()
    {
        if (_burstDot != null) return _burstDot;
        const int S = 8;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / (S * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                px[y * S + x] = new Color(1f, 1f, 1f, a * a);
            }
        tex.SetPixels(px);
        tex.Apply();
        _burstDot = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _burstDot;
    }
}
