using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Weapon : MonoBehaviour
{
    [SerializeField] public WeaponData originalWeaponData;
    [SerializeField] private WeaponData weaponData;
    [SerializeField] private PolygonCollider2D attackCollider;
    [SerializeField] private GameObject visual;

    private List<EnemyStats> hitEnemies = new List<EnemyStats>();
    private bool meleeHitSoundPlayedThisSwing = false;
    private PlayerStats playerStats;
    private bool isOnCooldown = false;
    public WeaponData defaultWeapon;
    public float grapplingDamage = 0f;

    // Input buffering
    private bool attackBuffered = false;
    private float bufferTimer = 0f;
    private const float BUFFER_WINDOW = 0.15f;

    //  Dual-wield: tool slot (right-click) 
    private WeaponData toolData;
    private WeaponData originalToolData;
    private bool isToolOnCooldown = false;
    private bool toolAttackBuffered = false;
    private float toolBufferTimer = 0f;
    private bool isToolRightHeld = false;

    // Obstacle drawer: "start as soon as cooldown clears" flag. Hold-to-use
    // tools don't fit the existing 0.15s buffer model — a player who places
    // an obstacle, then immediately repress-and-holds during the cooldown,
    // would have their press dropped because the buffer window expires
    // before the cooldown does. This flag persists until cooldown ends and
    // is validated against the real mouse state in Update so an early
    // release still aborts cleanly.
    private bool obstacleDrawerStartPending = false;

    // Subsystems — weapon side
    private FlamethrowerSystem flamethrowerSystem;
    private HammerSlamSystem hammerSlamSystem;

    // Subsystems — tool side
    private GrapplingHookSystem grapplingSystem;
    private ObstacleDrawerSystem obstacleDrawerSystem;
    private BombLauncherSystem bombLauncherSystem;
    private TrapLauncherSystem trapLauncherSystem;
    private TurretLauncherSystem turretLauncherSystem;
    private DecoyLauncherSystem decoyLauncherSystem;
    private ShieldSystem shieldSystem;
    private RevenantNecronomiconSystem bookSystem;
    private StealthCloakSystem stealthCloakSystem;

    // Persist flamethrower fuel across weapon swaps
    private float savedFlamethrowerFuel = -1f;
    private float flamethrowerUnequipTime = -1f;  // Time.time when flamethrower was unequipped
    private WeaponData savedFlamethrowerData;      // Cached reference for regen rate lookup

    // Weapon swap cooldown — prevents attack spam by scrolling
    [Header("Swap Cooldown")]
    [SerializeField] private float swapCooldownDuration = 0.25f;
    private float swapCooldownTimer = 0f;
    private bool IsSwapOnCooldown => swapCooldownTimer > 0f;

    //  Public accessors 
    public WeaponData GetWeaponData() => weaponData;
    public WeaponData GetToolData() => toolData;
    public bool HasTool => toolData != null;
    public ShieldSystem GetShieldSystem() => shieldSystem;

    // ── WeaponRollUI gauge queries ──
    // The WeaponRollUI draws a per-slot overlay for the CURRENTLY-EQUIPPED
    // weapon and tool. These expose the live state of the active subsystems.
    // They are only meaningful for the equipped items — an unequipped tool has
    // no running subsystem, so the UI shows no overlay on those slots.

    /// How a tool's gauge should be drawn this frame.
    public enum ToolGaugePhase
    {
        Ready,        // no overlay
        ActiveClock,  // effect is running — draw a depleting radial clock
        CooldownFill, // recharging — draw a rising fill gauge
    }

    /// Result of a tool gauge query.
    public struct ToolGaugeInfo
    {
        public bool has;              // false → this tool has no gauge
        public ToolGaugePhase phase;
        public float value;           // 0..1; meaning depends on phase:
                                      //  ActiveClock  → 1 = just started, 0 = about to end
                                      //  CooldownFill → 0 = just spent,   1 = ready
    }

    /// Gauge state for the currently-equipped TOOL (book or cloak).
    public ToolGaugeInfo GetToolGauge()
    {
        var info = new ToolGaugeInfo { has = false, phase = ToolGaugePhase.Ready, value = 1f };
        if (toolData == null) return info;

        if (toolData.isBook && bookSystem != null)
        {
            info.has = true;
            switch (bookSystem.CurrentPhase)
            {
                case RevenantNecronomiconSystem.BookPhase.AuraActive:
                    info.phase = ToolGaugePhase.ActiveClock;
                    info.value = bookSystem.AuraNormalized;     // 1→0 over the aura
                    break;
                case RevenantNecronomiconSystem.BookPhase.CoolingDown:
                    info.phase = ToolGaugePhase.CooldownFill;
                    info.value = bookSystem.CooldownNormalized; // 0→1 recharge
                    break;
                default:
                    info.phase = ToolGaugePhase.Ready;
                    info.value = 1f;
                    break;
            }
            return info;
        }

        if (toolData.isCloak && stealthCloakSystem != null)
        {
            info.has = true;
            if (stealthCloakSystem.IsInvisible)
            {
                // Cloak is up — depleting clock counting down the 30s.
                info.phase = ToolGaugePhase.ActiveClock;
                info.value = stealthCloakSystem.ActiveNormalized;
            }
            else if (stealthCloakSystem.IsOnCooldown)
            {
                // Recharging — rising fill gauge.
                info.phase = ToolGaugePhase.CooldownFill;
                info.value = stealthCloakSystem.CooldownNormalized;
            }
            else
            {
                info.phase = ToolGaugePhase.Ready;
                info.value = 1f;
            }
            return info;
        }

        return info;
    }

    /// True if the currently-equipped WEAPON is a flamethrower with a fuel
    /// gauge to display. (Out param gives 0..1 fuel: 1 = full.)
    public bool TryGetWeaponFuel(out float normalizedFuel)
    {
        normalizedFuel = 1f;
        if (weaponData != null && weaponData.isFlamethrower && flamethrowerSystem != null)
        {
            normalizedFuel = flamethrowerSystem.FuelNormalized;
            return true;
        }
        return false;
    }

    // The weapon-sprite GameObject's transform. Exposed so weapon subsystems
    // (e.g. HammerSlamSystem) can animate the held weapon during an attack.
    public Transform VisualTransform => visual != null ? visual.transform : null;

    // Returns true if the given WeaponData represents a shield tool
    // (has armorBonus but isn't another tool type like grappling hook).
    // Shield tools do NOT grant passive armor — their protection is
    // directional and only active while the shield is raised.
    private static bool IsShieldTool(WeaponData data)
    {
        if (data == null) return false;
        return data.armorBonus > 0f
            && !data.isGrapplingHook && !data.isObstacleDrawer
            && !data.isBombLauncher && !data.isTrap
            && !data.isTurret && !data.isDecoy
            && !data.isFlamethrower && !data.isRanged
            && !data.isBook && !data.isCloak;
    }

    public float GetFlamethrowerFuelNormalized()
    {
        if (flamethrowerSystem == null) return 1f;
        return flamethrowerSystem.FuelNormalized;
    }

    //  HOT-SWAP: Weapon (left-click slot)
    public void HotSwapWeapon(WeaponData newData)
    {
        if (newData == null) return;

        // If this is actually a tool, route to tool slot instead
        if (newData.IsTool)
        {
            HotSwapTool(newData);
            return;
        }

        // Ensure playerStats is set
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        // Clean up weapon-side subsystems
        if (flamethrowerSystem != null)
        {
            savedFlamethrowerFuel = flamethrowerSystem.CurrentFuel;
            flamethrowerUnequipTime = Time.time;
            savedFlamethrowerData = weaponData; // keep ref for regen rates
            flamethrowerSystem.Cleanup();
            flamethrowerSystem = null;
        }

        if (hammerSlamSystem != null)
        {
            hammerSlamSystem.Cleanup();
            hammerSlamSystem = null;
        }

        // Remove old armor bonus
        if (weaponData != null && weaponData.armorBonus > 0 && playerStats != null)
            playerStats.currentArmor -= weaponData.armorBonus;

        weaponData = newData.CreateRuntimeCopy();

        if (weaponData.armorBonus > 0 && playerStats != null)
            playerStats.currentArmor += weaponData.armorBonus;

        var sr = visual.GetComponent<SpriteRenderer>();
        if (sr != null && weaponData.sprite != null)
            sr.sprite = weaponData.sprite;

        ResizeCollider();

        if (weaponData.isFlamethrower)
        {
            flamethrowerSystem = new FlamethrowerSystem(this, weaponData);
            if (savedFlamethrowerFuel >= 0f)
            {
                float restoredFuel = CalculateBackgroundFuelRegen(savedFlamethrowerFuel);
                flamethrowerSystem.SetFuel(restoredFuel);
            }
            // Clear saved state now that we've applied it
            savedFlamethrowerFuel = -1f;
            flamethrowerUnequipTime = -1f;
            savedFlamethrowerData = null;
        }

        if (weaponData.isHammer)
            hammerSlamSystem = new HammerSlamSystem(this, weaponData);

        // Apply swap cooldown (cancel any existing attack cooldown too)
        ApplySwapCooldown();

        // Update cursor to reflect the weapon (left-hand determines cursor)
        UpdateWeaponCursor();
    }

    // Calculate how much fuel would have regenerated while the flamethrower was unequipped.
    private float CalculateBackgroundFuelRegen(float fuelAtUnequip)
    {
        if (flamethrowerUnequipTime < 0f) return fuelAtUnequip;

        // Use the saved flamethrower data for regen values, fall back to current weaponData
        WeaponData d = savedFlamethrowerData ?? weaponData;
        if (d == null) return fuelAtUnequip;

        float elapsed = Time.time - flamethrowerUnequipTime;

        // Respect the regen delay (same as FlamethrowerSystem does when not firing)
        float regenTime = elapsed - d.flameFuelRegenDelay;
        if (regenTime <= 0f) return fuelAtUnequip;

        float regened = fuelAtUnequip + d.flameFuelRegen * regenTime;
        return Mathf.Min(regened, d.flameFuelMax);
    }

    //  HOT-SWAP: Tool (right-click slot)
    public void HotSwapTool(WeaponData newData)
    {
        if (newData == null) return;

        // Ensure playerStats is set
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        // Clean up old tool subsystems
        CleanupToolSubsystems();

        // Remove old tool armor bonus (only for non-shield tools that grant passive armor)
        // Shield tools do NOT grant passive armor — their protection is directional + active only.
        if (toolData != null && toolData.armorBonus > 0 && !IsShieldTool(toolData) && playerStats != null)
            playerStats.currentArmor -= toolData.armorBonus;

        originalToolData = newData;
        toolData = newData.CreateRuntimeCopy();

        // Shield armor bonus is NOT applied passively.
        // Protection is only active while the shield is raised (right-click held)
        // and only blocks attacks from the cursor direction.
        // The ShieldSystem handles blocking via TryBlockOrParry().

        // Initialize tool subsystems
        if (toolData.isGrapplingHook) grapplingSystem = new GrapplingHookSystem(this, toolData);
        if (toolData.isObstacleDrawer) obstacleDrawerSystem = new ObstacleDrawerSystem(this, toolData);
        if (toolData.isBombLauncher) bombLauncherSystem = new BombLauncherSystem(this, toolData);
        if (toolData.isTrap) trapLauncherSystem = new TrapLauncherSystem(this, toolData);
        if (toolData.isTurret) turretLauncherSystem = new TurretLauncherSystem(this, toolData);
        if (toolData.isDecoy) decoyLauncherSystem = new DecoyLauncherSystem(this, toolData);
        if (toolData.isBook) bookSystem = new RevenantNecronomiconSystem(this, toolData);
        if (toolData.isCloak) stealthCloakSystem = new StealthCloakSystem(this, toolData);
        if (toolData.armorBonus > 0f) shieldSystem = new ShieldSystem(this, toolData);
    }

    private void CleanupToolSubsystems()
    {
        if (grapplingSystem != null) { grapplingSystem.Cleanup(); grapplingSystem = null; }
        if (obstacleDrawerSystem != null) { obstacleDrawerSystem.Cleanup(); obstacleDrawerSystem = null; }
        if (bombLauncherSystem != null) { bombLauncherSystem.Cleanup(); bombLauncherSystem = null; }
        if (trapLauncherSystem != null) { trapLauncherSystem.Cleanup(); trapLauncherSystem = null; }
        if (turretLauncherSystem != null) { turretLauncherSystem.Cleanup(); turretLauncherSystem = null; }
        if (decoyLauncherSystem != null) { decoyLauncherSystem.Cleanup(); decoyLauncherSystem = null; }
        if (shieldSystem != null) { shieldSystem.Cleanup(); shieldSystem = null; }
        if (bookSystem != null) { bookSystem.Cleanup(); bookSystem = null; }
        if (stealthCloakSystem != null) { stealthCloakSystem.Cleanup(); stealthCloakSystem = null; }

        // Any deferred drawer start belongs to the *old* tool — drop it.
        obstacleDrawerStartPending = false;
    }

    //  SWAP COOLDOWN
    private void ApplySwapCooldown()
    {
        swapCooldownTimer = swapCooldownDuration;
        isOnCooldown = true;
        attackBuffered = false;

        // Stop any running weapon cooldown coroutine — the swap cooldown replaces it
        StopAllCoroutines();

        if (attackCollider != null)
            attackCollider.enabled = false;
        hitEnemies.Clear();
    }

    //  INITIALIZATION
    private void Awake()
    {
        playerStats = GetComponentInParent<PlayerStats>();
        CreateRuntimeWeaponData();
        InitializeWeaponData();
        SetupWeapon();
        attackCollider.enabled = false;
    }

    private void CreateRuntimeWeaponData()
    {
        WeaponData sourceData = null;
        if (WeaponSelectionManager.Instance != null)
            sourceData = WeaponSelectionManager.Instance.GetChosenWeapon();
        if (sourceData == null) sourceData = originalWeaponData;
        if (sourceData == null) sourceData = defaultWeapon;

        if (sourceData != null)
        {
            if (sourceData.IsTool)
            {
                // Use default/fallback for weapon slot, put this in tool slot
                WeaponData fallbackWeapon = defaultWeapon ?? originalWeaponData;
                if (fallbackWeapon != null)
                    weaponData = fallbackWeapon.CreateRuntimeCopy();
                originalToolData = sourceData;
                toolData = sourceData.CreateRuntimeCopy();
            }
            else
            {
                weaponData = sourceData.CreateRuntimeCopy();
            }
        }
        else
            Debug.LogError("No weapon data available for runtime copy!");
    }

    private void InitializeWeaponData()
    {
        if (weaponData == null)
            Debug.LogError("Runtime weapon data is null!");
    }

    private void SetupWeapon()
    {
        if (weaponData == null) return;

        // playerStats already set in Awake, but ensure it's there
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        if (weaponData.armorBonus > 0 && playerStats != null)
            playerStats.currentArmor += weaponData.armorBonus;

        var spriteRenderer = visual.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && weaponData.sprite != null)
            spriteRenderer.sprite = weaponData.sprite;

        ResizeCollider();

        // Weapon-side subsystems
        if (weaponData.isFlamethrower)
            flamethrowerSystem = new FlamethrowerSystem(this, weaponData);

        if (weaponData.isHammer)
            hammerSlamSystem = new HammerSlamSystem(this, weaponData);

        // Tool-side subsystems
        if (toolData != null)
        {
            // Shield tools do NOT grant passive armor — protection is directional + active only.
            if (toolData.armorBonus > 0 && !IsShieldTool(toolData) && playerStats != null)
                playerStats.currentArmor += toolData.armorBonus;

            if (toolData.isGrapplingHook) grapplingSystem = new GrapplingHookSystem(this, toolData);
            if (toolData.isObstacleDrawer) obstacleDrawerSystem = new ObstacleDrawerSystem(this, toolData);
            if (toolData.isBombLauncher) bombLauncherSystem = new BombLauncherSystem(this, toolData);
            if (toolData.isTrap) trapLauncherSystem = new TrapLauncherSystem(this, toolData);
            if (toolData.isTurret) turretLauncherSystem = new TurretLauncherSystem(this, toolData);
            if (toolData.isDecoy) decoyLauncherSystem = new DecoyLauncherSystem(this, toolData);
            if (toolData.isBook) bookSystem = new RevenantNecronomiconSystem(this, toolData);
            if (toolData.isCloak) stealthCloakSystem = new StealthCloakSystem(this, toolData);
            if (toolData.armorBonus > 0f) shieldSystem = new ShieldSystem(this, toolData);
        }

        // Set initial cursor
        UpdateWeaponCursor();
    }

    public void ResetToOriginalStats()
    {
        if (originalWeaponData != null)
            weaponData = originalWeaponData.CreateRuntimeCopy();
    }

    //  UPDATE
    private void Update()
    {
        // Tick swap cooldown
        if (swapCooldownTimer > 0f)
        {
            swapCooldownTimer -= Time.deltaTime;
            if (swapCooldownTimer <= 0f)
            {
                swapCooldownTimer = 0f;
                isOnCooldown = false;
            }
        }

        // Weapon-side updates
        UpdateFlamethrowerSystem();

        // Tool-side updates
        UpdateGrapplingSystem();
        UpdateObstacleDrawerSystem();
        UpdateBombLauncherSystem();
        UpdateTrapSystem();
        UpdateTurretSystem();
        UpdateDecoySystem();
        UpdateShieldSystem();
        UpdateBookSystem();
        UpdateCloakSystem();

        // Weapon input buffer
        if (attackBuffered)
        {
            bufferTimer -= Time.deltaTime;
            if (bufferTimer <= 0f)
            {
                attackBuffered = false;
            }
            else if (!isOnCooldown)
            {
                attackBuffered = false;
                ExecuteWeaponAttack();
            }
        }

        // Tool input buffer
        if (toolAttackBuffered)
        {
            toolBufferTimer -= Time.deltaTime;
            if (toolBufferTimer <= 0f)
            {
                toolAttackBuffered = false;
            }
            else if (!isToolOnCooldown)
            {
                toolAttackBuffered = false;
                ExecuteToolAttack();
            }
        }

        // Obstacle drawer: deferred start across a cooldown window.
        // If the player kept right-click held through the cooldown, start
        // drawing the moment it clears. If they released early, drop the
        // pending flag silently — same effect as if they'd never pressed.
        if (obstacleDrawerStartPending)
        {
            bool rightStillDown = UnityEngine.InputSystem.Mouse.current != null
                && UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;

            // Clear the pending flag if the tool changed, the player let go,
            // or the drawer no longer exists — these are all "user no longer
            // wants this draw" or "we can't service it" outcomes.
            if (!rightStillDown
                || toolData == null
                || !toolData.isObstacleDrawer
                || obstacleDrawerSystem == null)
            {
                obstacleDrawerStartPending = false;
            }
            else if (!isToolOnCooldown)
            {
                // Cooldown is done and the button is still held — fulfil the
                // queued start. ExecuteToolAttack performs the placement-mode
                // and stamina gates internally, so we don't need to duplicate
                // them here.
                obstacleDrawerStartPending = false;
                ExecuteToolAttack();
            }
        }
    }

    private void UpdateGrapplingSystem()
    {
        if (toolData?.isGrapplingHook != true || grapplingSystem == null) return;
        bool inPlacementMode = TowerPlacementManager.Instance?.IsInPlacementMode() == true;
        grapplingSystem.SetActive(!inPlacementMode);
        if (!inPlacementMode) grapplingSystem.Update();
    }

    private void UpdateObstacleDrawerSystem()
    {
        if (toolData?.isObstacleDrawer != true || obstacleDrawerSystem == null) return;
        bool inPlacementMode = TowerPlacementManager.Instance?.IsInPlacementMode() == true;
        if (!inPlacementMode) obstacleDrawerSystem.Update();

        // If the drawer auto-finished a valid obstacle (player held right-click
        // past the gameplay cap, weaponData.drawDuration — e.g. 1s), charge
        // stamina + start a cooldown now. Same costs as a normal release-to-
        // finish path. Without this, hitting the cap would be a free obstacle.
        if (obstacleDrawerSystem.ConsumeAutoFinishSignal())
        {
            if (playerStats != null)
                playerStats.TryConsumeStamina(playerStats.obstacleDrawerStaminaCost);

            StartCoroutine(ToolCooldownRoutine());
        }
    }

    private void UpdateFlamethrowerSystem()
    {
        if (weaponData?.isFlamethrower != true || flamethrowerSystem == null) return;
        bool inPlacementMode = TowerPlacementManager.Instance?.IsInPlacementMode() == true;
        if (!inPlacementMode)
        {
            // Continuous stamina drain while actively firing. Auto-stop firing
            // when stamina runs out so the flame can't be held with empty bar.
            if (flamethrowerSystem.IsFiring && playerStats != null)
            {
                bool depleted = playerStats.DrainStamina(
                    playerStats.flamethrowerStaminaDrainPerSec * Time.deltaTime);
                if (depleted)
                    flamethrowerSystem.StopFiring();
            }

            flamethrowerSystem.Update();
        }
    }

    private void UpdateBombLauncherSystem()
    {
        if (toolData?.isBombLauncher != true || bombLauncherSystem == null) return;
        bombLauncherSystem.Update();
    }

    private void UpdateTrapSystem()
    {
        if (toolData?.isTrap != true || trapLauncherSystem == null) return;
        trapLauncherSystem.Update();
    }

    private void UpdateTurretSystem()
    {
        if (toolData?.isTurret != true || turretLauncherSystem == null) return;
        turretLauncherSystem.Update();
    }

    private void UpdateDecoySystem()
    {
        if (toolData?.isDecoy != true || decoyLauncherSystem == null) return;
        decoyLauncherSystem.Update();
    }

    private void UpdateBookSystem()
    {
        if (toolData?.isBook != true || bookSystem == null) return;
        bookSystem.Update();
    }

    private void UpdateCloakSystem()
    {
        if (toolData?.isCloak != true || stealthCloakSystem == null) return;
        stealthCloakSystem.Update();
    }

    private void UpdateShieldSystem()
    {
        if (shieldSystem == null) return;
        shieldSystem.Update();
    }

    //  Shield raise/lower (called by PlayerAttack) 
    public void RaiseShield()
    {
        if (shieldSystem != null)
            shieldSystem.RaiseShield();
    }

    public void LowerShield()
    {
        if (shieldSystem != null)
            shieldSystem.LowerShield();
    }

    private void ResizeCollider()
    {
        if (attackCollider != null && weaponData != null)
            attackCollider.transform.localScale = weaponData.size;
    }

    //  WEAPON ATTACK (Left-click)

    // Breaks Stealth Cloak invisibility when the player commits an offensive
    // action. Called from the weapon attack paths (melee swing, ranged shot,
    // boomerang, flamethrower start, hammer slam) and from melee hit-connect.
    // Safe to call when no cloak is equipped — it simply does nothing.
    private void NotifyCloakOffensiveAttack()
    {
        if (stealthCloakSystem != null)
            stealthCloakSystem.NotifyPlayerAttackedEnemy();
    }

    public void PerformAttack()
    {
        if (isOnCooldown)
        {
            // The hammer is a hold-to-charge weapon — a press can't be buffered
            // (the player would have to keep holding through the whole cooldown
            // for it to make sense, and a buffered charge with the button
            // already released would hang). Just ignore presses on cooldown.
            if (weaponData != null && weaponData.isHammer)
                return;

            if (!attackBuffered)
            {
                attackBuffered = true;
                bufferTimer = BUFFER_WINDOW;
            }
            return;
        }
        ExecuteWeaponAttack();
    }

    // Battle Hammer — called when the left mouse button is RELEASED.
    // Releases a charged slam (damage scales with how long the button was held).
    // No-op for non-hammer weapons or when no charge is in progress.
    public void ReleaseHammerCharge()
    {
        // A release also cancels a buffered press so a charge can't auto-start
        // after the button is already up.
        attackBuffered = false;

        if (weaponData == null || !weaponData.isHammer) return;
        if (hammerSlamSystem == null) return;
        if (!hammerSlamSystem.IsCharging) return; // nothing charging → nothing to release

        // Out of stamina → cancel the charge instead of slamming (mirrors how
        // a normal melee swing is gated on stamina).
        if (playerStats != null && !playerStats.TryConsumeStamina(playerStats.meleeAttackStaminaCost))
        {
            hammerSlamSystem.CancelCharge();
            return;
        }

        // Fire the charged slam. ReleaseCharge transitions the charging runner
        // straight into its slam using the accumulated charge factor.
        hammerSlamSystem.ReleaseCharge();
        NotifyCloakOffensiveAttack(); // the slam is an offensive action — breaks stealth

        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlaySFX(FMODEvents.instance.meleeSwing, transform.position);

        // Cooldown starts on RELEASE so holding a charge doesn't burn it.
        StartCoroutine(WeaponCooldownRoutine());
    }

    private void ExecuteWeaponAttack()
    {
        if (weaponData == null) return;

        if (weaponData.isFlamethrower)
        {
            // Flamethrower: stamina is drained continuously while firing
            // (see UpdateFlamethrowerSystem). Just gate the initial start
            // on having any stamina at all so it doesn't sputter instantly.
            if (flamethrowerSystem != null && flamethrowerSystem.CanFire()
                && playerStats != null && playerStats.HasStamina(0.05f))
            {
                flamethrowerSystem.StartFiring();
                NotifyCloakOffensiveAttack(); // committing to an attack breaks stealth
            }
        }
        else if (weaponData.isBoomerang)
        {
            // Boomerang is classified as ranged (see WeaponData.OnValidate)
            if (playerStats != null && !playerStats.TryConsumeStamina(playerStats.rangedAttackStaminaCost))
                return;

            ShootBoomerang();
            NotifyCloakOffensiveAttack();
            StartCoroutine(WeaponCooldownRoutine());
        }
        else if (weaponData.isRanged)
        {
            if (playerStats != null && !playerStats.TryConsumeStamina(playerStats.rangedAttackStaminaCost))
                return;

            ShootProjectile();
            NotifyCloakOffensiveAttack();
            StartCoroutine(WeaponCooldownRoutine());
        }
        else if (weaponData.isHammer)
        {
            // Battle Hammer — slow, heavy melee with a hold-to-charge attack.
            // PerformAttack() here corresponds to the button being PRESSED:
            // begin charging. The actual slam (damage, VFX, cooldown) fires on
            // release via ReleaseHammerCharge(). Stamina is consumed at release.
            if (hammerSlamSystem == null)
                hammerSlamSystem = new HammerSlamSystem(this, weaponData);

            hammerSlamSystem.BeginCharge();
        }
        else
        {
            // Melee
            if (playerStats != null && !playerStats.TryConsumeStamina(playerStats.meleeAttackStaminaCost))
                return;

            // Always play the swing sound — hit sound layers on top if it connects
            if (AudioManager.instance != null && FMODEvents.instance != null)
                AudioManager.instance.PlaySFX(FMODEvents.instance.meleeSwing, transform.position);

            NotifyCloakOffensiveAttack(); // swinging at enemies breaks stealth
            StartCoroutine(AttackRoutine());
            StartCoroutine(WeaponCooldownRoutine());
        }
    }

    public void StopAttack()
    {
        if (weaponData?.isFlamethrower == true && flamethrowerSystem != null)
        {
            if (flamethrowerSystem.IsFiring)
                flamethrowerSystem.StopFiring();
        }
    }

    //  TOOL ATTACK (Right-click)
    public void PerformToolAttack()
    {
        if (toolData == null) return;

        if (isToolOnCooldown)
        {
            // Obstacle drawer is a hold-to-use tool — the standard 0.15s
            // buffer is too short and discrete-shaped for this. Instead,
            // remember that the player wants to start drawing once the
            // cooldown clears. Update will check this flag against the
            // current mouse state to start the draw if (and only if) the
            // player is still holding right-click when cooldown ends.
            if (toolData.isObstacleDrawer)
            {
                obstacleDrawerStartPending = true;
                return;
            }

            if (!toolAttackBuffered)
            {
                toolAttackBuffered = true;
                toolBufferTimer = BUFFER_WINDOW;
            }
            return;
        }
        ExecuteToolAttack();
    }

    private void ExecuteToolAttack()
    {
        if (toolData == null) return;

        if (toolData.isObstacleDrawer)
        {
            if (obstacleDrawerSystem != null && !obstacleDrawerSystem.IsDrawing())
            {
                // Don't start a draw we know can't progress — UpdateObstacleDrawerSystem
                // short-circuits in placement mode, so a draw started there would
                // silently record nothing, then cancel on release. Previously this
                // still charged stamina and started a cooldown; gating up front
                // avoids both costs.
                if (TowerPlacementManager.Instance?.IsInPlacementMode() == true)
                    return;

                // Gate (but don't consume) stamina. Drawer now follows the same
                // "no whiff tax" rule as the grappling hook: stamina is only
                // deducted on a successful draw (path long enough to produce
                // an obstacle), and that deduction happens in StopToolAttack
                // when StopDrawing reports success. This stops the old failure
                // mode where a brief / stationary press cost stamina AND a
                // cooldown despite producing nothing visible.
                if (playerStats != null && !playerStats.HasStamina(playerStats.obstacleDrawerStaminaCost))
                    return;

                obstacleDrawerSystem.StartDrawing();
            }
        }
        else if (toolData.isGrapplingHook)
        {
            if (grapplingSystem?.CanFire() == true)
            {
                // Gate (but don't deduct) on stamina first — exhausted player
                // shouldn't be able to fire, same rule as melee/ranged attacks.
                if (playerStats != null && !playerStats.HasStamina(playerStats.grapplingHookStaminaCost))
                    return;

                // Then fire. FireHook returns false if there's no valid target,
                // in which case we don't charge stamina (no "whiff tax").
                bool fired = grapplingSystem.FireHook();
                if (!fired) return;

                if (playerStats != null)
                    playerStats.TryConsumeStamina(playerStats.grapplingHookStaminaCost);

                StartCoroutine(ToolCooldownRoutine());
            }
        }
        else if (toolData.isBombLauncher)
        {
            if (bombLauncherSystem != null)
            {
                bombLauncherSystem.PlaceMine();
                StartCoroutine(ToolCooldownRoutine());
            }
        }
        else if (toolData.isTrap)
        {
            if (trapLauncherSystem != null)
            {
                trapLauncherSystem.PlaceTrap();
                StartCoroutine(ToolCooldownRoutine());
            }
        }
        else if (toolData.isTurret)
        {
            if (turretLauncherSystem != null)
            {
                turretLauncherSystem.PlaceTurret();
                StartCoroutine(ToolCooldownRoutine());
            }
        }
        else if (toolData.isDecoy)
        {
            if (decoyLauncherSystem != null)
            {
                decoyLauncherSystem.PlaceDecoy();
                StartCoroutine(ToolCooldownRoutine());
            }
        }
        else if (toolData.isBook)
        {
            // Right-click TOGGLES the book (first cast → aura, second →
            // ends it early and enters cooldown). The book owns its complete
            // two-phase cooldown internally (RevenantNecronomiconSystem), so
            // we do NOT start the generic ToolCooldownRoutine — doing so would
            // gate (and drop) the second click that performs the toggle-off.
            if (bookSystem != null)
                bookSystem.Activate();
        }
        else if (toolData.isCloak)
        {
            // Right-click TOGGLES the cloak (first → invisible, second →
            // uncloak early + cooldown). Like the book, the cloak owns its
            // own duration + cooldown (PlayerCloakEffect), so we must NOT
            // start the generic ToolCooldownRoutine — it would block the
            // toggle-off click whenever the asset's attackCooldown is > 0.
            if (stealthCloakSystem != null)
                stealthCloakSystem.Activate();
        }
        else if (toolData.armorBonus > 0f)
        {
            // Shield — raise handled via OnToolButtonPressed/Released in PlayerAttack
            // ExecuteToolAttack is a no-op for shield; blocking is passive while held.
        }
    }

    public void StopToolAttack()
    {
        if (toolData?.isObstacleDrawer == true && obstacleDrawerSystem != null)
        {
            if (obstacleDrawerSystem.IsDrawing())
            {
                // StopDrawing returns true only when an obstacle was actually
                // placed. Pair the cooldown + stamina deduction with that
                // outcome so a brief or stationary press is genuinely free —
                // matches the "no whiff tax" rule used by the grappling hook.
                bool created = obstacleDrawerSystem.StopDrawing();
                if (created)
                {
                    if (playerStats != null)
                        playerStats.TryConsumeStamina(playerStats.obstacleDrawerStaminaCost);

                    StartCoroutine(ToolCooldownRoutine());
                }
            }
        }
    }

    public void OnToolButtonPressed()
    {
        isToolRightHeld = true;
        UpdateToolCursor();
    }

    public void OnToolButtonReleased()
    {
        isToolRightHeld = false;
        UpdateWeaponCursor();
    }

    //  CURSOR MANAGEMENT
    private void UpdateWeaponCursor()
    {
        if (CursorManager.Instance == null || weaponData == null) return;

        if (weaponData.isFlamethrower)
            CursorManager.Instance.SetCursor(CursorManager.CursorType.Flamethrower);
        else if (weaponData.isBoomerang)
            CursorManager.Instance.SetCursor(CursorManager.CursorType.Boomerang);
        else if (weaponData.isHammer)
            CursorManager.Instance.SetCursor(CursorManager.CursorType.Hammer);
        else if (weaponData.isRanged)
            CursorManager.Instance.SetCursor(CursorManager.CursorType.Ranged);
        else
            CursorManager.Instance.SetCursor(CursorManager.CursorType.Melee);
    }

    private void UpdateToolCursor()
    {
        if (CursorManager.Instance == null || toolData == null) return;

        if (toolData.isGrapplingHook) CursorManager.Instance.SetCursor(CursorManager.CursorType.Hook);
        else if (toolData.isObstacleDrawer) CursorManager.Instance.SetCursor(CursorManager.CursorType.ObstacleDrawer);
        else if (toolData.isBombLauncher) CursorManager.Instance.SetCursor(CursorManager.CursorType.BombLauncher);
        else if (toolData.isTrap) CursorManager.Instance.SetCursor(CursorManager.CursorType.Trap);
        else if (toolData.isTurret) CursorManager.Instance.SetCursor(CursorManager.CursorType.Turret);
        else if (toolData.isDecoy) CursorManager.Instance.SetCursor(CursorManager.CursorType.Decoy);
        else if (toolData.isBook) CursorManager.Instance.SetCursor(CursorManager.CursorType.Book);
        else if (toolData.isCloak) CursorManager.Instance.SetCursor(CursorManager.CursorType.Cloak);
        else if (toolData.armorBonus > 0f) CursorManager.Instance.SetCursor(CursorManager.CursorType.Shield);
    }

    //  COOLDOWN ROUTINES
    private IEnumerator WeaponCooldownRoutine()
    {
        isOnCooldown = true;
        yield return new WaitForSeconds(weaponData.attackCooldown);
        isOnCooldown = false;
    }

    private IEnumerator ToolCooldownRoutine()
    {
        isToolOnCooldown = true;
        yield return new WaitForSeconds(toolData.attackCooldown);
        isToolOnCooldown = false;
    }

    //  PROJECTILE & MELEE
    private void ShootProjectile()
    {
        Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        Vector2 direction = (Camera.main.ScreenToWorldPoint(mousePos) - transform.position).normalized;

        GameObject projectile = Instantiate(weaponData.projectilePrefab, transform.position, Quaternion.identity);
        var weaponProjectile = projectile.GetComponent<WeaponProjectile>();
        weaponProjectile?.Initialize(direction, weaponData.damage, weaponData.projectileSpeed, weaponData.knockBackForce);
    }

    //  BOOMERANG — creates the GO entirely from code, no prefab needed
    private void ShootBoomerang()
    {
        Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        Vector2 direction = (Camera.main.ScreenToWorldPoint(mousePos) - transform.position).normalized;

        // Build the boomerang GameObject from scratch
        GameObject go = new GameObject("Boomerang");
        go.transform.position = transform.position;

        // Physics — trigger collider + kinematic body so OnTriggerEnter2D fires
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        var col = go.AddComponent<CircleCollider2D>();
        col.radius = 0.35f;
        col.isTrigger = true;

        // The boomerang script — handles movement, damage, VFX
        var bp = go.AddComponent<BoomerangProjectile>();

        // Player root for the return trip
        Transform playerRoot = playerStats != null ? playerStats.transform : (transform.parent ?? transform);

        bp.Initialize(
            playerRoot,
            direction,
            weaponData.damage,
            weaponData.projectileSpeed,
            weaponData.boomerangRange,
            weaponData.knockBackForce,
            weaponData.boomerangCurve
        );
    }

    private IEnumerator AttackRoutine()
    {
        hitEnemies.Clear();
        meleeHitSoundPlayedThisSwing = false;
        attackCollider.enabled = true;
        yield return new WaitForSeconds(weaponData.attackCooldown);
        attackCollider.enabled = false;
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (!attackCollider.enabled || !other.CompareTag("Enemy")) return;

        var enemy = other.GetComponent<EnemyStats>();
        if (enemy == null || hitEnemies.Contains(enemy)) return;

        hitEnemies.Add(enemy);

        if (weaponData.damage > 0)
        {
            // Landing a melee hit on an enemy definitively breaks stealth, even
            // if (somehow) the swing-start notification was missed.
            NotifyCloakOffensiveAttack();

            var stats = other.GetComponent<CharacterStats>();
            if (stats != null)
            {
                float damage = weaponData.damage;

                // Apply parry damage bonus if enemy is stunned
                var parryEffect = other.GetComponent<ParryStunEffect>();
                if (parryEffect != null)
                    damage *= parryEffect.DamageMultiplier;

                stats.TakeDamage(damage);
            }

            var vampireEffect = playerStats?.GetComponent<EnergyVampireTouchEffect>();
            if (vampireEffect != null)
                vampireEffect.DrainEnergy();

            // Play hit sound once per swing, even if multiple enemies are hit.
            // Layers on top of the swing sound for a satisfying impact.
            if (!meleeHitSoundPlayedThisSwing && AudioManager.instance != null && FMODEvents.instance != null)
            {
                AudioManager.instance.PlaySFX(FMODEvents.instance.meleeHit, other.transform.position);
                meleeHitSoundPlayedThisSwing = true;
            }

            // ── Combat Feel ──
            CombatJuice.OnPlayerHitEnemy(other.gameObject, isMelee: true);
        }

        if (weaponData.knockBack)
            ApplyKnockback(enemy);
    }

    private void ApplyKnockback(EnemyStats enemy)
    {
        Vector2 direction = GetKnockbackDirection(enemy.transform.position);
        var enemyController = enemy.GetComponent<EnemyController>();
        enemyController?.ApplyKnockback(direction, weaponData.knockBackForce);
    }

    private Vector2 GetKnockbackDirection(Vector3 enemyPosition)
    {
        Transform refTransform = playerStats != null ? playerStats.transform : transform;
        Vector2 direction = (enemyPosition - refTransform.position).normalized;
        if (direction.sqrMagnitude < 1e-4f)
        {
            direction = (enemyPosition - transform.position).normalized;
            if (direction.sqrMagnitude < 1e-4f)
                direction = Random.insideUnitCircle.normalized;
        }
        return direction;
    }

    void OnDestroy()
    {
        if (flamethrowerSystem != null) flamethrowerSystem.Cleanup();
        if (hammerSlamSystem != null) hammerSlamSystem.Cleanup();
        CleanupToolSubsystems();
    }
}
