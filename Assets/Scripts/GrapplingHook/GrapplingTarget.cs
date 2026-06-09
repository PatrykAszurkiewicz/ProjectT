using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

public class GrapplingHookSystem
{
    private Weapon weapon;
    private WeaponData weaponData;
    private Transform playerTransform;
    private Rigidbody2D playerRigidbody;
    private PlayerMovement playerMovement;

    // Targeting
    private List<IGrapplingTarget> potentialTargets = new List<IGrapplingTarget>();
    private IGrapplingTarget currentTarget;
    private Dictionary<IGrapplingTarget, Color> originalColors = new Dictionary<IGrapplingTarget, Color>();

    // Visual
    private LineRenderer lineRenderer;
    private GrapplingHookTargetIndicator currentIndicator;
    private Sprite hookSprite;

    // Hook head visual (dark half-circle at the tip of the line)
    private GameObject hookHeadObject;
    private SpriteRenderer hookHeadRenderer;

    // Line disintegration effect
    private bool isDisintegrating = false;
    private Coroutine disintegrationCoroutine;

    // State
    private enum HookState { Idle, Shooting, Connected, Retracting }
    private HookState currentState = HookState.Idle;
    private bool isOnCooldown = false;
    private bool isActive = true;

    // Mass-based grappling constants
    private const float HEAVY_ENEMY_THRESHOLD = 100f; // kg - above this, player is pulled to enemy
    private const float LIGHT_ENEMY_THRESHOLD = 20f;  // kg - below this, enemy is pulled very fast
    private const float MASS_SPEED_FACTOR = 0.02f;    // How much mass affects pull speed

    public GrapplingHookSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.weaponData = data;

        playerTransform = weapon.GetComponentInParent<PlayerMovement>()?.transform ?? weapon.transform.parent;
        playerRigidbody = weapon.GetComponentInParent<Rigidbody2D>();
        playerMovement = weapon.GetComponentInParent<PlayerMovement>();

        SetupLineRenderer();
        LoadHookSprite();
        RefreshTargets();
    }

    private void LoadHookSprite()
    {
        // Try loading the sprite from Resources
        hookSprite = Resources.Load<Sprite>("Sprites/Cursors/Hook");

        if (hookSprite == null)
        {
            var sprites = Resources.LoadAll<Sprite>("Sprites/Cursors/Hook");
            if (sprites?.Length > 0)
                hookSprite = sprites[0];
        }

        // TODO remove alternative sprites for hook 
        if (hookSprite == null)
        {
            string[] paths = { "Sprites/Cursors/Hook", "Sprites/hook" };
            foreach (string path in paths)
            {
                hookSprite = Resources.Load<Sprite>(path);
                if (hookSprite != null) break;
            }
        }

        // Create fallback sprite if still null
        if (hookSprite == null)
            hookSprite = CreateSimpleHookSprite();
    }

    private Sprite CreateSimpleHookSprite()
    {
        const int size = 32;
        Texture2D texture = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];
        Color hookColor = new Color(1f, 0.8f, 0f, 1f);

        // Initialize transparent
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.clear;

        // Draw simple hook shape
        DrawHookShape(pixels, size, hookColor);

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    private void DrawHookShape(Color[] pixels, int size, Color hookColor)
    {
        // Vertical line (hook body)
        for (int y = size / 4; y < size * 3 / 4; y++)
            for (int x = size / 2 - 1; x <= size / 2 + 1; x++)
                if (IsValidPixel(x, y, size))
                    pixels[y * size + x] = hookColor;

        // Horizontal line (hook curve)
        for (int x = size / 2; x < size * 3 / 4; x++)
            for (int y = size * 3 / 4 - 1; y <= size * 3 / 4 + 1; y++)
                if (IsValidPixel(x, y, size))
                    pixels[y * size + x] = hookColor;

        // Hook tip
        int centerX = size * 3 / 4;
        int centerY = size * 3 / 4;
        for (int x = centerX - 3; x <= centerX + 3; x++)
            for (int y = centerY; y <= centerY + 6; y++)
                if (IsValidPixel(x, y, size))
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(centerX, centerY));
                    if (dist <= 3f && x >= centerX - 1)
                        pixels[y * size + x] = hookColor;
                }
    }

    private bool IsValidPixel(int x, int y, int size) => x >= 0 && x < size && y >= 0 && y < size;

    private void CreateHookHead()
    {
        if (hookHeadObject != null) return;

        hookHeadObject = new GameObject("GrapplingHookHead");

        // Render a small 3-prong grappling claw. Sprite "up" (positive Y in
        // texture space) points along the direction of travel, with the rope
        // attachment pivot at the bottom center.
        const int texSize = 96;
        Texture2D tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[texSize * texSize];

        Color metalDark = new Color(0.11f, 0.09f, 0.09f, 1f); // outline / shadow
        Color metalMid = new Color(0.18f, 0.16f, 0.15f, 1f); // main body
        Color metalHi = new Color(0.32f, 0.29f, 0.26f, 1f); // highlight edge

        // Clear
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        float cx = texSize * 0.5f;

        // --- Central shaft (vertical neck where the rope attaches) ---
        // Sits low in the sprite so the claws can curve above it.
        float shaftHalfW = texSize * 0.06f;
        float shaftBottom = texSize * 0.05f;
        float shaftTop = texSize * 0.42f;
        for (int y = (int)shaftBottom; y <= (int)shaftTop; y++)
        {
            for (int x = (int)(cx - shaftHalfW - 1.5f); x <= (int)(cx + shaftHalfW + 1.5f); x++)
            {
                if (x < 0 || x >= texSize || y < 0 || y >= texSize) continue;
                float dx = Mathf.Abs(x - cx);
                if (dx <= shaftHalfW)
                {
                    // Center highlight strip on the shaft
                    Color c = (dx < shaftHalfW * 0.35f) ? metalHi : metalMid;
                    pixels[y * texSize + x] = c;
                }
                else if (dx <= shaftHalfW + 1.2f)
                {
                    pixels[y * texSize + x] = metalDark; // outline
                }
            }
        }

        // --- Three curved prongs ---
        // Center prong points straight up, side prongs splay outward.
        // Each prong is a sampled curve with a thickness, drawn as disks.
        DrawProng(pixels, texSize, cx, texSize * 0.38f, 0f, texSize * 0.48f, metalMid, metalDark, metalHi);
        DrawProng(pixels, texSize, cx, texSize * 0.38f, -0.85f, texSize * 0.44f, metalMid, metalDark, metalHi);
        DrawProng(pixels, texSize, cx, texSize * 0.38f, 0.85f, texSize * 0.44f, metalMid, metalDark, metalHi);

        tex.SetPixels(pixels);
        tex.Apply();

        // Pivot at bottom-center so rotating around Z pivots on the rope end.
        Sprite hookClaw = Sprite.Create(tex, new Rect(0, 0, texSize, texSize), new Vector2(0.5f, 0.08f), 100f);

        hookHeadRenderer = hookHeadObject.AddComponent<SpriteRenderer>();
        hookHeadRenderer.sprite = hookClaw;
        hookHeadRenderer.sortingOrder = 3000;
        hookHeadRenderer.enabled = false;

        // Small, natural-looking tip.
        hookHeadObject.transform.localScale = Vector3.one * 0.35f;
    }

    /// Draws a curved prong from (baseX, baseY) arcing outward. `splay` is
    /// the horizontal bias: 0 = straight up, negative = curves left,
    /// positive = curves right. `length` is prong length in pixels.
    private void DrawProng(Color[] pixels, int texSize, float baseX, float baseY,
                           float splay, float length,
                           Color body, Color outline, Color highlight)
    {
        const int samples = 28;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)(samples - 1); // 0..1 from base to tip

            // Quadratic curve: base -> control point -> tip.
            // Tip hooks inward slightly (opposite splay near the very tip)
            // for the classic claw shape.
            float ctrlX = baseX + splay * length * 0.55f;
            float ctrlY = baseY + length * 0.55f;
            float tipX = baseX + splay * length * 0.95f - splay * length * 0.25f;
            float tipY = baseY + length * 0.95f;

            float omt = 1f - t;
            float px = omt * omt * baseX + 2f * omt * t * ctrlX + t * t * tipX;
            float py = omt * omt * baseY + 2f * omt * t * ctrlY + t * t * tipY;

            // Thickness tapers from thick at base to a sharp point at tip.
            float thickness = Mathf.Lerp(texSize * 0.065f, texSize * 0.012f, t);

            DrawDisk(pixels, texSize, px, py, thickness + 1.2f, outline);
            DrawDisk(pixels, texSize, px, py, thickness, body);

            // Inner highlight on the leading edge (side opposite the splay curve).
            float hx = px - Mathf.Sign(splay == 0f ? 1f : splay) * thickness * 0.35f;
            float hy = py + thickness * 0.15f;
            DrawDisk(pixels, texSize, hx, hy, thickness * 0.35f, highlight);
        }
    }

    private void DrawDisk(Color[] pixels, int texSize, float cx, float cy, float radius, Color color)
    {
        int x0 = Mathf.Max(0, (int)(cx - radius - 1));
        int x1 = Mathf.Min(texSize - 1, (int)(cx + radius + 1));
        int y0 = Mathf.Max(0, (int)(cy - radius - 1));
        int y1 = Mathf.Min(texSize - 1, (int)(cy + radius + 1));
        float r2 = radius * radius;

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                if (dx * dx + dy * dy <= r2)
                    pixels[y * texSize + x] = color;
            }
        }
    }

    private void ShowHookHead(Vector3 position, Vector3 direction)
    {
        if (hookHeadObject == null) CreateHookHead();

        hookHeadObject.transform.position = position;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        hookHeadObject.transform.rotation = Quaternion.Euler(0, 0, angle);

        hookHeadRenderer.enabled = true;
    }

    private void HideHookHead()
    {
        if (hookHeadRenderer != null)
            hookHeadRenderer.enabled = false;
    }

    private void DestroyHookHead()
    {
        if (hookHeadObject != null)
        {
            Object.Destroy(hookHeadObject);
            hookHeadObject = null;
            hookHeadRenderer = null;
        }
    }

    public void SetActive(bool active)
    {
        isActive = active;
        if (!active)
        {
            // Safety net: if the weapon is deactivated mid-pull (e.g. weapon
            // swap), release control of the player so they don't get stuck
            // with PlayerMovement disabled.
            if (playerMovement != null)
                playerMovement.IsBeingGrappled = false;

            ClearCurrentTarget();
            HideIndicator();
            HideHookHead();

            StopLineDisintegration();
            if (lineRenderer != null)
            {
                lineRenderer.enabled = false;
                ResetLineRenderer();
            }

            originalColors.Clear();
        }
    }

    private void SetupLineRenderer()
    {
        lineRenderer = weapon.GetComponent<LineRenderer>();
        if (lineRenderer == null)
            lineRenderer = weapon.gameObject.AddComponent<LineRenderer>();

        // Ensure the component exists before setting properties
        if (lineRenderer != null)
        {
            lineRenderer.material = new Material(Shader.Find("Sprites/Default")) { color = weaponData.hookLineColor };
            lineRenderer.startWidth = weaponData.lineWidth;
            lineRenderer.endWidth = weaponData.lineWidth * 0.5f;
            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = true;
            lineRenderer.sortingOrder = 3000; // Above grass Y-sort range
            lineRenderer.enabled = false;
        }
    }

    private void ResetLineRenderer()
    {
        if (lineRenderer == null) return;

        // Reset all line renderer properties
        lineRenderer.material.color = weaponData.hookLineColor;
        lineRenderer.startWidth = weaponData.lineWidth;
        lineRenderer.endWidth = weaponData.lineWidth * 0.5f;
        lineRenderer.positionCount = 2;
    }

    private void RefreshTargets()
    {
        potentialTargets.Clear();

        // Find all valid targets
        AddTargetsFromComponents<Tower>();
        AddTargetFromComponent<CentralCore>();
        AddTargetsFromComponents<EnemyStats>();
        AddTargetsFromTag("Obstacle");
    }

    private void AddTargetsFromComponents<T>() where T : Component
    {
        foreach (var component in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
            AddTarget(component.gameObject);
    }

    private void AddTargetFromComponent<T>() where T : Component
    {
        var component = Object.FindAnyObjectByType<T>();
        if (component != null) AddTarget(component.gameObject);
    }

    private void AddTargetsFromTag(string tag)
    {
        foreach (var obj in GameObject.FindGameObjectsWithTag(tag))
            AddTarget(obj);
    }

    private void AddTarget(GameObject obj)
    {
        if (!IsValidGameObject(obj)) return;

        var target = obj.GetComponent<GrapplingTarget>() ?? obj.AddComponent<GrapplingTarget>();
        if (target?.CanBeGrappled() == true)
            potentialTargets.Add(target);
    }

    private bool IsValidGameObject(GameObject obj) => obj != null && obj.activeInHierarchy;

    public void Update()
    {
        if (!isActive) return;

        // Validate current target
        if (currentTarget != null && !IsTargetValid(currentTarget))
        {
            HandleDestroyedTarget();
        }

        // Periodic refresh
        if (Time.frameCount % 30 == 0)
            RefreshTargets();

        // Update targeting only when idle
        if (currentState == HookState.Idle)
        {
            UpdateTargeting();
        }

        // Update line renderer when shooting or connected
        if ((currentState == HookState.Shooting || currentState == HookState.Connected) &&
            currentTarget != null && IsTargetValid(currentTarget) && !isDisintegrating && lineRenderer.enabled)
        {
            lineRenderer.SetPosition(0, playerTransform.position);

            if (currentState == HookState.Connected)
            {
                lineRenderer.SetPosition(1, currentTarget.GetGrapplePoint());
            }
        }
    }
    private void ReapplyHighlight(IGrapplingTarget target)
    {
        if (target == null) return;
        var targetTransform = target.GetTransform();
        var spriteRenderer = targetTransform?.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && originalColors.ContainsKey(target))
        {
            Color expectedColor = Color.Lerp(originalColors[target], weaponData.targetHighlightColor, 0.5f);
            // Only reapply if the color has been changed by something else
            if (spriteRenderer.color != expectedColor)
            {
                spriteRenderer.color = expectedColor;
            }
        }
    }

    private bool IsTargetValid(IGrapplingTarget target)
    {
        if (target == null) return false;
        var transform = target.GetTransform();
        return transform != null && transform.gameObject != null && target.CanBeGrappled();
    }

    private void HandleDestroyedTarget()
    {
        HideIndicator();
        HideHookHead();

        if (currentState == HookState.Shooting || currentState == HookState.Connected)
        {
            // Start disintegration effect instead of immediately disabling
            StartLineDisintegration();
        }
        else
        {
            currentTarget = null;
        }
    }
    private void UpdateTargeting()
    {
        Vector3 playerPos = playerTransform.position;
        Vector3 cursorWorldPos = GetCursorWorldPosition();
        Vector3 targetDirection = (cursorWorldPos - playerPos).normalized;

        IGrapplingTarget bestTarget = FindBestTarget(playerPos, targetDirection);

        // Only update if target changed
        if (bestTarget != currentTarget)
        {
            ClearCurrentTarget();
            HideIndicator();

            UpdateCursor(bestTarget);

            if (bestTarget != null)
            {
                currentTarget = bestTarget;
                AddHighlight(currentTarget);
                ShowIndicator(currentTarget);
            }
        }
        else
        {
            // Still update cursor even if target hasn't changed
            UpdateCursor(bestTarget);
        }
    }

    private IGrapplingTarget FindBestTarget(Vector3 playerPos, Vector3 targetDirection)
    {
        CleanInvalidTargets();

        IGrapplingTarget bestTarget = null;
        float bestScore = float.MaxValue;

        foreach (var target in potentialTargets)
        {
            if (!IsTargetValid(target)) continue;

            Vector3 targetPos = target.GetGrapplePoint();
            float distance = Vector3.Distance(playerPos, targetPos);
            if (distance > weaponData.hookRange) continue;

            Vector3 dirToTarget = (targetPos - playerPos).normalized;
            float angle = Vector3.Angle(targetDirection, dirToTarget);
            if (angle > weaponData.targetingAngle) continue;

            float score = distance + (angle * 0.1f);
            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = target;
            }
        }

        return bestTarget;
    }

    private void CleanInvalidTargets()
    {
        for (int i = potentialTargets.Count - 1; i >= 0; i--)
        {
            var target = potentialTargets[i];
            if (!IsTargetValid(target))
            {
                originalColors.Remove(target);
                potentialTargets.RemoveAt(i);
            }
        }
    }

    private void UpdateCursor(IGrapplingTarget bestTarget)
    {
        if (CursorManager.Instance != null &&
            (TowerPlacementManager.Instance == null || !TowerPlacementManager.Instance.IsInPlacementMode()))
        {
            // Use Hook (black) when no target, HookHighlight (green) when targeting
            var cursorType = bestTarget != null ? CursorManager.CursorType.HookHightlight : CursorManager.CursorType.Hook;

            //Debug.Log($"[GRAPPLING] UpdateCursor - BestTarget: {bestTarget != null}, CursorType: {cursorType}");

            CursorManager.Instance.SetCursor(cursorType);
        }
    }

    private void ShowIndicator(IGrapplingTarget target)
    {
        if (hookSprite != null && isActive && currentIndicator == null)
        {
            var targetTransform = target.GetTransform();
            if (targetTransform != null)
            {
                // Only show indicator if target is visible on screen
                Vector3 screenPos = Camera.main.WorldToScreenPoint(targetTransform.position);
                const float margin = 100f;
                bool isVisible = screenPos.z > 0 &&
                               screenPos.x >= -margin &&
                               screenPos.x <= Screen.width + margin &&
                               screenPos.y >= -margin &&
                               screenPos.y <= Screen.height + margin;

                if (isVisible)
                {
                    currentIndicator = GrapplingHookTargetIndicator.CreateIndicator(targetTransform, hookSprite);
                }
            }
        }
    }

    private void HideIndicator()
    {
        if (currentIndicator != null)
        {
            currentIndicator.Hide();
            currentIndicator = null;
        }
    }

    private void ClearCurrentTarget()
    {
        if (currentTarget != null)
        {
            RemoveHighlight(currentTarget);
            currentTarget = null;
        }
    }

    private Vector3 GetCursorWorldPosition()
    {
        var cursorPointer = Object.FindAnyObjectByType<CursorPointer>();
        if (cursorPointer != null)
            return cursorPointer.transform.position;

        if (Mouse.current != null)
        {
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            mousePos.z = 0;
            return mousePos;
        }

        return playerTransform.position + Vector3.up;
    }

    private void AddHighlight(IGrapplingTarget target)
    {
        var targetTransform = target?.GetTransform();
        var spriteRenderer = targetTransform?.GetComponent<SpriteRenderer>();

        if (spriteRenderer != null && !originalColors.ContainsKey(target))
        {
            originalColors[target] = spriteRenderer.color;
            spriteRenderer.color = Color.Lerp(spriteRenderer.color, weaponData.targetHighlightColor, 0.5f);

            // Tell tower to pause visual updates
            var tower = targetTransform.GetComponent<Tower>();
            if (tower != null)
            {
                tower.SetGrapplingTarget(true);
            }
        }
    }

    private void RemoveHighlight(IGrapplingTarget target)
    {
        var targetTransform = target?.GetTransform();
        var spriteRenderer = targetTransform?.GetComponent<SpriteRenderer>();

        if (spriteRenderer != null && originalColors.TryGetValue(target, out Color originalColor))
        {
            spriteRenderer.color = originalColor;
            originalColors.Remove(target);

            // Tell tower to resume visual updates
            var tower = targetTransform.GetComponent<Tower>();
            if (tower != null)
            {
                tower.SetGrapplingTarget(false);
            }
        }
    }

    public bool CanFire() => !isOnCooldown && currentState == HookState.Idle;

    /// <summary>
    /// Returns true if the hook actually fired at a valid target.
    /// Returns false if there is no current target or the target is invalid —
    /// callers can use this to skip costs like stamina drain on a "whiffed" press.
    /// </summary>
    public bool FireHook()
    {
        if (!CanFire() || currentTarget == null) return false;

        // Validate target before firing
        if (!IsTargetValid(currentTarget))
        {
            currentTarget = null;
            return false;
        }

        // Stop any ongoing disintegration effect
        StopLineDisintegration();

        weapon.StartCoroutine(GrappleSequence());
        return true;
    }

    private IEnumerator GrappleSequence()
    {
        currentState = HookState.Shooting;
        HideIndicator();

        // Play grappling hook shoot sound effect
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.grapplingHookShoot, playerTransform.position);
        }

        var grappleTarget = currentTarget;
        if (!IsTargetValid(grappleTarget))
        {
            currentState = HookState.Idle;
            yield break;
        }

        Vector3 startPos = playerTransform.position;
        Vector3 targetPos = grappleTarget.GetGrapplePoint();
        float distance = Vector3.Distance(startPos, targetPos);
        float travelTime = distance / weaponData.hookSpeed;

        // Animate hook shooting
        yield return AnimateHookShooting(startPos, targetPos, travelTime);

        HideHookHead();

        // Execute pull sequence if target still valid
        if (IsTargetValid(grappleTarget))
        {
            currentState = HookState.Connected;
            grappleTarget.OnGrappleHit(null);
            yield return ExecutePullSequence(grappleTarget);
        }

        EndGrappleSequence();
    }

    private IEnumerator AnimateHookShooting(Vector3 startPos, Vector3 targetPos, float travelTime)
    {
        lineRenderer.enabled = true;
        lineRenderer.SetPosition(0, startPos);
        lineRenderer.SetPosition(1, startPos);

        Vector3 direction = (targetPos - startPos).normalized;

        float elapsed = 0f;
        while (elapsed < travelTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / travelTime;
            Vector3 currentEnd = Vector3.Lerp(startPos, targetPos, t);
            lineRenderer.SetPosition(1, currentEnd);

            ShowHookHead(currentEnd, direction);

            yield return null;
        }
    }

    // Mass-based pull sequence
    private IEnumerator ExecutePullSequence(IGrapplingTarget grappleTarget)
    {

        // Deal damage to enemy targets when hook connects
        DealGrapplingDamage(grappleTarget);

        // Long enough that a max-range hook completes even at moderate pull
        // speeds. Loop exits early once the player arrives anyway.
        float pullDuration = 3.0f;
        float elapsed = 0f;

        // Determine pull behavior based on target type and mass
        bool shouldPullPlayer = ShouldPullPlayer(grappleTarget);

        // Take ownership of the player's movement so PlayerMovement.FixedUpdate
        // stops calling MovePosition and clobbering our pull each physics step.
        if (shouldPullPlayer && playerMovement != null)
            playerMovement.IsBeingGrappled = true;

        try
        {
            while (elapsed < pullDuration && IsTargetValid(grappleTarget))
            {
                elapsed += Time.deltaTime;

                if (shouldPullPlayer)
                    PullPlayerTowardsTarget(grappleTarget.GetGrapplePoint(), grappleTarget);
                else
                    PullTargetTowardsPlayer(grappleTarget, playerTransform.position);

                float dist = Vector3.Distance(playerTransform.position, grappleTarget.GetGrapplePoint());
                if (dist < 2.5f) break;

                yield return null;
            }
        }
        finally
        {
            // Always return control to PlayerMovement, even on early exit.
            if (shouldPullPlayer && playerMovement != null)
                playerMovement.IsBeingGrappled = false;

            // Clear any residual velocity so the player doesn't drift after
            // the hook releases.
            if (shouldPullPlayer && playerRigidbody != null)
                playerRigidbody.linearVelocity = Vector2.zero;
        }
    }
    private void DealGrapplingDamage(IGrapplingTarget target)
    {
        // Only deal damage if weapon has grappling damage configured
        if (weapon.grapplingDamage <= 0f) return;

        // Only deal damage to enemies (not towers, obstacles, or core)
        var targetTransform = target.GetTransform();
        if (targetTransform == null) return;

        // MODIFIED: Check for EnemyStats first
        var enemyStats = targetTransform.GetComponent<EnemyStats>();
        if (enemyStats != null && !enemyStats.IsDead())
        {
            enemyStats.TakeDamage(weapon.grapplingDamage);
            Debug.Log($"[GRAPPLING_HOOK] Dealt {weapon.grapplingDamage} damage to {targetTransform.name}");

            // Play impact sound
            if (AudioManager.instance != null && FMODEvents.instance != null)
            {
                //TODO add audio to the grappling hook hit
                //AudioManager.instance.PlayOneShot(FMODEvents.instance.enemyHit, targetTransform.position);
            }
            return;
        }

        // Check for IDamageable targets (like BossHead)
        var damageable = targetTransform.GetComponent<IDamageable>();
        if (damageable != null && damageable.CanTakeDamage())
        {
            // Check if it's not a tower or core (we don't want to damage friendly structures)
            bool isTowerOrCore = targetTransform.GetComponent<Tower>() != null ||
                                 targetTransform.GetComponent<CentralCore>() != null;

            if (!isTowerOrCore)
            {
                damageable.TakeDamage(weapon.grapplingDamage, weapon.gameObject);
                Debug.Log($"[GRAPPLING_HOOK] Dealt {weapon.grapplingDamage} damage to {targetTransform.name} (IDamageable)");

                // Play impact sound
                if (AudioManager.instance != null && FMODEvents.instance != null)
                {
                    //TODO add audio to the grappling hook hit
                    //AudioManager.instance.PlayOneShot(FMODEvents.instance.enemyHit, targetTransform.position);
                }
            }
        }
    }

    // Determine if player should be pulled based on target type and mass
    private bool ShouldPullPlayer(IGrapplingTarget target)
    {
        // For non-enemies (towers, obstacles, core), use original solid target logic
        if (target.IsSolidTarget())
            return true;

        // For enemies, check mass to determine pull direction
        var enemyMass = GetTargetMass(target);
        if (enemyMass >= HEAVY_ENEMY_THRESHOLD)
        {
            // Heavy enemies are immovable - pull player to them
            return true;
        }

        // Light and medium enemies are pulled to player
        return false;
    }

    // Get mass from target if it's an enemy
    private float GetTargetMass(IGrapplingTarget target)
    {
        var targetTransform = target.GetTransform();
        if (targetTransform != null)
        {
            var enemyStats = targetTransform.GetComponent<EnemyStats>();
            if (enemyStats != null)
            {
                return enemyStats.Mass;
            }
        }
        return 0f; // Non-enemies have no mass consideration
    }

    // Mass-aware player pulling.
    //
    // IMPORTANT: PlayerMovement drives the player via rb.MovePosition() in
    // FixedUpdate, NOT via linearVelocity. Setting linearVelocity here would
    // do nothing — MovePosition overwrites position every physics step. So
    // we use MovePosition too, and PlayerMovement.IsBeingGrappled gates its
    // own MovePosition call while we own the rigidbody.
    private void PullPlayerTowardsTarget(Vector3 targetPoint, IGrapplingTarget target)
    {
        if (playerRigidbody == null) return;

        Vector3 toTarget = targetPoint - playerTransform.position;
        float distance = toTarget.magnitude;
        if (distance < 0.001f) return;
        Vector3 direction = toTarget / distance;

        // Pull speed in units/sec. With default pullForce = 15, this gives
        // ~22 u/s — significantly faster than the player's walk speed.
        float pullSpeed = weaponData.pullForce * 1.5f;

        // Heavy enemies act as a solid anchor — slightly faster pull toward them.
        if (target != null)
        {
            var enemyMass = GetTargetMass(target);
            if (enemyMass >= HEAVY_ENEMY_THRESHOLD)
            {
                float massMultiplier = Mathf.Clamp(enemyMass / HEAVY_ENEMY_THRESHOLD, 1f, 1.5f);
                pullSpeed *= massMultiplier;
            }
        }

        // Distance to move this frame. Clamp to remaining distance so we
        // don't overshoot on the final step.
        float step = pullSpeed * Time.deltaTime;
        if (step > distance) step = distance;

        // Drive the rigidbody the same way PlayerMovement does — MovePosition.
        Vector2 newPos = (Vector2)playerTransform.position + (Vector2)direction * step;
        playerRigidbody.MovePosition(newPos);
    }

    // Mass-aware target pulling  
    private void PullTargetTowardsPlayer(IGrapplingTarget target, Vector3 playerPosition)
    {
        if (target is GrapplingTarget grapplingTarget)
        {
            Vector3 direction = (playerPosition - target.GetGrapplePoint()).normalized;

            // Calculate mass-based pull force
            var enemyMass = GetTargetMass(target);
            float massBasedForce = CalculateMassBasedPullForce(enemyMass);

            grapplingTarget.ApplyGrapplePull(direction, weaponData.pullForce * Time.deltaTime * 143f * massBasedForce);
        }
    }

    // Calculate pull force multiplier based on enemy mass
    private float CalculateMassBasedPullForce(float mass)
    {
        if (mass <= 0f) return 1f; // Non-enemies use default force

        if (mass <= LIGHT_ENEMY_THRESHOLD)
        {
            // Light enemies: 1.5x to 2x force
            return 1.5f + (LIGHT_ENEMY_THRESHOLD - mass) / LIGHT_ENEMY_THRESHOLD * 0.5f;
        }
        else if (mass < HEAVY_ENEMY_THRESHOLD)
        {
            // Medium enemies: Force inversely proportional to mass
            float normalizedMass = (mass - LIGHT_ENEMY_THRESHOLD) / (HEAVY_ENEMY_THRESHOLD - LIGHT_ENEMY_THRESHOLD);
            return Mathf.Lerp(1.5f, 0.3f, normalizedMass); // From 1.5x down to 0.3x force
        }

        // Heavy enemies should not reach this function
        return 0.1f;
    }

    // TODO remove legacy methods
    private void PullPlayer(Vector3 targetPoint)
    {
        PullPlayerTowardsTarget(targetPoint, null);
    }

    private void PullTarget(IGrapplingTarget target, Vector3 playerPosition)
    {
        PullTargetTowardsPlayer(target, playerPosition);
    }

    private void EndGrappleSequence()
    {
        currentState = HookState.Retracting;

        HideHookHead();

        // Start disintegration
        StartLineDisintegration();

        weapon.StartCoroutine(EndGrappleSequenceCoroutine());
    }

    private IEnumerator EndGrappleSequenceCoroutine()
    {
        currentTarget?.OnGrappleRelease();
        currentTarget = null;
        currentState = HookState.Idle;

        weapon.StartCoroutine(CooldownCoroutine());
        yield break;
    }

    // Line Disintegration Methods
    private void StartLineDisintegration()
    {
        if (lineRenderer == null || !lineRenderer.enabled) return;

        // Stop any existing disintegration
        StopLineDisintegration();

        isDisintegrating = true;
        disintegrationCoroutine = weapon.StartCoroutine(DisintegrationEffect());
    }

    private void StopLineDisintegration()
    {
        if (disintegrationCoroutine != null)
        {
            weapon.StopCoroutine(disintegrationCoroutine);
            disintegrationCoroutine = null;
        }
        isDisintegrating = false;
    }

    private IEnumerator DisintegrationEffect()
    {
        const float disintegrationDuration = 0.8f;
        const int segmentCount = 10; // Number of segments to break the line into

        if (lineRenderer == null)
        {
            isDisintegrating = false;
            yield break;
        }

        // Store original positions
        Vector3 startPos = lineRenderer.GetPosition(0);
        Vector3 endPos = lineRenderer.GetPosition(1);

        // Create multiple segments for the disintegration effect
        lineRenderer.positionCount = segmentCount;

        // Initialize segments
        for (int i = 0; i < segmentCount; i++)
        {
            float t = (float)i / (segmentCount - 1);
            Vector3 segmentPos = Vector3.Lerp(startPos, endPos, t);
            lineRenderer.SetPosition(i, segmentPos);
        }

        float elapsed = 0f;
        Color originalColor = lineRenderer.material.color;

        while (elapsed < disintegrationDuration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / disintegrationDuration;

            // Fade out effect
            float alpha = Mathf.Lerp(1f, 0f, progress * progress); // Quadratic fade for smoother effect
            lineRenderer.material.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);

            // Shrink width effect
            float widthMultiplier = Mathf.Lerp(1f, 0.1f, progress);
            lineRenderer.startWidth = weaponData.lineWidth * widthMultiplier;
            lineRenderer.endWidth = weaponData.lineWidth * 0.5f * widthMultiplier;

            // Break apart effect
            float breakApartIntensity = Mathf.Lerp(0f, 2f, progress * progress);
            for (int i = 1; i < segmentCount - 1; i++) // Skip first and last segment
            {
                float segmentT = (float)i / (segmentCount - 1);
                Vector3 originalSegmentPos = Vector3.Lerp(startPos, endPos, segmentT);

                // Add random drift to middle segments
                Vector3 randomOffset = new Vector3(
                    Random.Range(-breakApartIntensity, breakApartIntensity),
                    Random.Range(-breakApartIntensity, breakApartIntensity),
                    0f
                );

                lineRenderer.SetPosition(i, originalSegmentPos + randomOffset);
            }

            yield return null;
        }

        // Disable the line renderer
        lineRenderer.enabled = false;

        // Reset properties for next use
        ResetLineRenderer();

        isDisintegrating = false;
        disintegrationCoroutine = null;
    }

    private IEnumerator CooldownCoroutine()
    {
        isOnCooldown = true;
        yield return new WaitForSeconds(CooldownModifier.Apply(weaponData.attackCooldown));
        isOnCooldown = false;
    }

    public void Cleanup()
    {
        HideIndicator();
        DestroyHookHead();
        StopLineDisintegration();

        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
            ResetLineRenderer();
        }
    }
}

// Grappling Target Interface - UNCHANGED
public interface IGrapplingTarget
{
    bool CanBeGrappled();
    Vector3 GetGrapplePoint();
    bool IsSolidTarget();
    void OnGrappleHit(object hook);
    void OnGrappleRelease();
    Transform GetTransform();
}

public class GrapplingTarget : MonoBehaviour, IGrapplingTarget
{
    public bool canBeGrappled = true;
    public bool isSolidTarget = true;
    public Vector3 grapplePointOffset = Vector3.zero;

    /// Extra offset for the hook indicator icon, set dynamically by bosses.
    [HideInInspector] public Vector3 indicatorExtraOffset = Vector3.zero;

    private Rigidbody2D rb;
    private bool isBeingGrappled = false;
    private bool isDestroyed = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        DetermineTargetType();

        // If this is a Boss1, pull grappling offsets immediately so they're
        // never zero — this handles the case where GrapplingHookSystem adds
        // the GrapplingTarget component after Boss1.Start() has already run.
        var boss = GetComponent<Boss1>();
        if (boss != null)
            boss.ApplyGrapplingOffsets(this);
    }

    private void DetermineTargetType()
    {
        if (GetComponent<Tower>() || GetComponent<CentralCore>() || CompareTag("Obstacle"))
        {
            isSolidTarget = true;
        }
        else if (GetComponent<EnemyStats>())
        {
            // Scarecrow is a stationary support enemy — treat it as solid
            // (tower-like) so the grappling hook pulls the PLAYER to IT
            // instead of yanking the scarecrow toward the player. Without
            // this branch, the enemy default isSolidTarget=false runs and
            // ApplyGrapplePull dumps force into the scarecrow's rigidbody.
            // With zero linear damping on its prefab, that velocity never
            // decays and the scarecrow ends up shoving the player around.
            if (GetComponent<Scarecrow>() != null)
            {
                isSolidTarget = true;
            }
            else
            {
                isSolidTarget = false;
                if (rb == null)
                {
                    rb = gameObject.AddComponent<Rigidbody2D>();
                    rb.gravityScale = 0;
                }
            }
        }
    }

    private void OnDestroy()
    {
        isDestroyed = true;
        isBeingGrappled = false;
    }

    public bool CanBeGrappled()
    {
        if (isDestroyed || this == null || gameObject == null) return false;
        if (!canBeGrappled) return false;

        if (IsComponentDestroyed()) return false;

        // Reject targets that are currently faded out / "ghosted" — e.g. the
        // Scarecrow in its hidden-cycle phase. Without this check, the player
        // could lock on to an invisible target, fire the hook, and watch it
        // pull them to thin air. Two checks:
        //
        //   1) The Scarecrow component owns its own visibility flag. Asking
        //      it directly is cheaper than reading a SpriteRenderer.
        //   2) Fallback: sprite alpha. Anything < 0.05 counts as invisible.
        //      Catches generic fade-out animations on other potential targets.
        var scarecrow = GetComponent<Scarecrow>();
        if (scarecrow != null && !scarecrow.IsCurrentlyVisible()) return false;

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.color.a < 0.05f) return false;

        return true;
    }

    private bool IsComponentDestroyed()
    {
        var tower = GetComponent<Tower>();
        if (tower?.IsDestroyed() == true) return true;

        var core = GetComponent<CentralCore>();
        if (core?.IsDestroyed() == true) return true;

        var enemy = GetComponent<EnemyStats>();
        if (enemy?.IsDead() == true) return true;

        return false;
    }

    public Vector3 GetGrapplePoint()
    {
        if (isDestroyed || this == null || gameObject == null) return Vector3.zero;
        return transform.position + grapplePointOffset;
    }

    public bool IsSolidTarget() => !isDestroyed && isSolidTarget;

    public Transform GetTransform()
    {
        if (isDestroyed || this == null || gameObject == null) return null;
        return transform;
    }

    public bool IsBeingGrappled() => !isDestroyed && isBeingGrappled;

    public void OnGrappleHit(object hook)
    {
        if (isDestroyed) return;

        isBeingGrappled = true;
        GetComponent<EnemyController>()?.SetGrapplingState(true, 2f);
    }

    public void OnGrappleRelease()
    {
        if (isDestroyed) return;

        isBeingGrappled = false;
        GetComponent<EnemyController>()?.SetGrapplingState(false);
    }

    public void ApplyGrapplePull(Vector3 direction, float force)
    {
        if (isDestroyed || rb == null || isSolidTarget) return;

        rb.AddForce(direction * force * 1.45f, ForceMode2D.Impulse);
        rb.AddForce(direction * force * 2.8f, ForceMode2D.Force);

        Vector3 pullVelocity = direction * (force * 0.145f);
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, pullVelocity, 1.0f);
    }
}
