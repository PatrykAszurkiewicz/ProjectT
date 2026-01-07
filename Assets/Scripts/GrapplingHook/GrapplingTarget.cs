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

    // Targeting
    private List<IGrapplingTarget> potentialTargets = new List<IGrapplingTarget>();
    private IGrapplingTarget currentTarget;
    private Dictionary<IGrapplingTarget, Color> originalColors = new Dictionary<IGrapplingTarget, Color>();

    // Visual
    private LineRenderer lineRenderer;
    private GrapplingHookTargetIndicator currentIndicator;
    private Sprite hookSprite;

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

    public void SetActive(bool active)
    {
        isActive = active;
        if (!active)
        {
            ClearCurrentTarget();
            HideIndicator();

            // Stop any disintegration effect and hide line
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
            lineRenderer.sortingOrder = 100;
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
            UpdateTargeting();

        // Update line renderer when shooting or connected
        if ((currentState == HookState.Shooting || currentState == HookState.Connected) &&
            currentTarget != null && IsTargetValid(currentTarget) && !isDisintegrating && lineRenderer.enabled)
        {
            // Always update the start position to follow the player
            lineRenderer.SetPosition(0, playerTransform.position);

            // Only update end position during connected state (shooting animation handles its own end position)
            if (currentState == HookState.Connected)
            {
                lineRenderer.SetPosition(1, currentTarget.GetGrapplePoint());
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
        ClearCurrentTarget();
        HideIndicator();

        Vector3 playerPos = playerTransform.position;
        Vector3 cursorWorldPos = GetCursorWorldPosition();
        Vector3 targetDirection = (cursorWorldPos - playerPos).normalized;

        IGrapplingTarget bestTarget = FindBestTarget(playerPos, targetDirection);

        // Update cursor only when not in placement mode
        UpdateCursor(bestTarget);

        if (bestTarget != null)
        {
            currentTarget = bestTarget;
            AddHighlight(currentTarget);
            ShowIndicator(currentTarget);
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
                currentIndicator = GrapplingHookTargetIndicator.CreateIndicator(targetTransform, hookSprite);
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
        }
    }

    public bool CanFire() => !isOnCooldown && currentState == HookState.Idle;

    public void FireHook()
    {
        if (!CanFire() || currentTarget == null) return;

        // Validate target before firing
        if (!IsTargetValid(currentTarget))
        {
            currentTarget = null;
            return;
        }

        // Stop any ongoing disintegration effect
        StopLineDisintegration();

        weapon.StartCoroutine(GrappleSequence());
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

        float elapsed = 0f;
        while (elapsed < travelTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / travelTime;
            Vector3 currentEnd = Vector3.Lerp(startPos, targetPos, t);
            lineRenderer.SetPosition(1, currentEnd);
            yield return null;
        }
    }

    // Mass-based pull sequence
    private IEnumerator ExecutePullSequence(IGrapplingTarget grappleTarget)
    {

        // Deal damage to enemy targets when hook connects
        DealGrapplingDamage(grappleTarget);

        float pullDuration = 1.5f;
        float elapsed = 0f;
        Vector3 lastPlayerPosition = playerTransform.position;

        // Determine pull behavior based on target type and mass
        bool shouldPullPlayer = ShouldPullPlayer(grappleTarget);

        while (elapsed < pullDuration && IsTargetValid(grappleTarget))
        {
            elapsed += Time.deltaTime;

            if (shouldPullPlayer)
                PullPlayerTowardsTarget(grappleTarget.GetGrapplePoint(), grappleTarget);
            else
                PullTargetTowardsPlayer(grappleTarget, playerTransform.position);

            float dist = Vector3.Distance(playerTransform.position, grappleTarget.GetGrapplePoint());
            if (dist < 2.0f) break;

            // Check if player moved significantly after being close to target - early release
            if (dist < 3.0f)
            {
                float playerMovement = Vector3.Distance(playerTransform.position, lastPlayerPosition);
                if (playerMovement > 1.5f)
                {
                    break; // Player is actively moving away - release early
                }
            }

            lastPlayerPosition = playerTransform.position;
            yield return null;
        }
    }

    private void DealGrapplingDamage(IGrapplingTarget target)
    {
        // Only deal damage if weapon has grappling damage configured
        if (weapon.grapplingDamage <= 0f) return;

        // Only deal damage to enemies (not towers, obstacles, or core)
        var targetTransform = target.GetTransform();
        if (targetTransform == null) return;

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

    // Mass-aware player pulling
    private void PullPlayerTowardsTarget(Vector3 targetPoint, IGrapplingTarget target)
    {
        if (playerRigidbody == null) return;

        Vector3 direction = (targetPoint - playerTransform.position).normalized;
        float distance = Vector3.Distance(playerTransform.position, targetPoint);
        float forceMagnitude = weaponData.pullForce * Mathf.Clamp01(distance / 5f);

        // Apply mass-based force adjustment for heavy enemies
        var enemyMass = GetTargetMass(target);
        if (enemyMass >= HEAVY_ENEMY_THRESHOLD)
        {
            // Heavy enemies provide stronger pull
            float massMultiplier = Mathf.Clamp(enemyMass / HEAVY_ENEMY_THRESHOLD, 1f, 2f);
            forceMagnitude *= massMultiplier;
        }

        playerRigidbody.AddForce(direction * forceMagnitude * 16f);

        Vector3 pullVelocity = direction * (weaponData.pullForce * 0.08f);
        playerRigidbody.linearVelocity = Vector3.Lerp(playerRigidbody.linearVelocity, pullVelocity, Time.deltaTime * 1.05f);
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
        yield return new WaitForSeconds(weaponData.attackCooldown);
        isOnCooldown = false;
    }

    public void Cleanup()
    {
        HideIndicator();
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

    private Rigidbody2D rb;
    private bool isBeingGrappled = false;
    private bool isDestroyed = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        DetermineTargetType();
    }

    private void DetermineTargetType()
    {
        if (GetComponent<Tower>() || GetComponent<CentralCore>() || CompareTag("Obstacle"))
        {
            isSolidTarget = true;
        }
        else if (GetComponent<EnemyStats>())
        {
            isSolidTarget = false;
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0;
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

        return !IsComponentDestroyed();
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