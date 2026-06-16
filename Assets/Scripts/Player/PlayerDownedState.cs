using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


// Co-op: per-player DOWNED state. Instead of dying outright, a player
// in co-op enters a revivable downed state — control + collider off, pinned at
// 0 HP, ignored by enemies — and waits for a teammate to revive them (Phase 7b)
// or for the team to wipe.
// Single player NEVER enters this state: PlayerStats.Die() only routes here when
// PlayerRegistry.Count > 1, so the N=1 death path (destroy) is byte-identical.
[DisallowMultipleComponent]
public class PlayerDownedState : MonoBehaviour
{
    [Header("Revive")]
    [Tooltip("Fraction of max health restored when revived.")]
    [Range(0.05f, 1f)] public float reviveHealthPercent = 0.5f;
    [Tooltip("Seconds of damage immunity granted right after a revive.")]
    public float reviveInvulnerabilitySeconds = 2f;

    [Header("Downed visual tint (fallback when there is no PlayerMovement)")]
    [Tooltip("Tint applied to the player sprite while downed, if PlayerMovement " +
             "isn't present to play the prone animation.")]
    public Color downedTint = new Color(0.5f, 0.5f, 0.55f, 0.9f);

    /// <summary>True while this player is downed (alive object, 0 HP, awaiting revive).</summary>
    public bool IsDowned { get; private set; }

    /// <summary>Raised when THIS player goes down / is revived. Arg = this player's stats.</summary>
    public event Action<PlayerStats> OnDowned;
    public event Action<PlayerStats> OnRevived;

    /// <summary>Global hooks for UI / orchestrator (resolve which player via PlayerRef).</summary>
    public static event Action<PlayerStats> AnyDowned;
    public static event Action<PlayerStats> AnyRevived;

    private PlayerStats _stats;
    private PlayerMovement _movement;
    private PlayerInput _input;
    private PlayerAttack _attack;
    private PlayerAim _aim;
    private PlayerTowerPlacer _placer;
    private SpriteRenderer _sprite;
    private Color _spriteBaseColor;
    private readonly List<Collider2D> _disabledColliders = new List<Collider2D>();

    private void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        _movement = GetComponent<PlayerMovement>();
        _input = GetComponent<PlayerInput>();
        _attack = GetComponent<PlayerAttack>();
        _aim = GetComponent<PlayerAim>();
        _placer = GetComponent<PlayerTowerPlacer>();
        _sprite = GetComponentInChildren<SpriteRenderer>();
        if (_sprite != null) _spriteBaseColor = _sprite.color;
    }

    /// <summary>
    /// Enter the downed state. Idempotent — always returns true once the player is
    /// downed, so the Die() caller reliably skips the destroy path in co-op.
    /// </summary>
    public bool EnterDowned()
    {
        if (IsDowned) return true;
        if (_stats == null) _stats = GetComponent<PlayerStats>();
        IsDowned = true;

        // Pin at 0 HP so the registry treats this player as out of the fight
        // (IsDead() == true → skipped by selectors, counted by AllDead).
        if (_stats != null) _stats.SetHealthAndNotify(0f);

        // Freeze this player's control (per-player, so the teammate is unaffected).
        if (_input != null) _input.DeactivateInput();
        if (_attack != null) _attack.SetSuppressed(true);
        if (_placer != null) _placer.enabled = false; // its OnDisable exits placement cleanly
        if (_aim != null) _aim.enabled = false;        // hide this player's reticle/cursor

        // Downed visual.
        if (_movement != null) _movement.EnterDownedVisual();
        else ApplyTint(true);

        // Collider(s) off so enemies pass over the body and contact damage stops.
        _disabledColliders.Clear();
        foreach (var c in GetComponentsInChildren<Collider2D>(false))
        {
            if (c != null && c.enabled) { c.enabled = false; _disabledColliders.Add(c); }
        }

        OnDowned?.Invoke(_stats);
        AnyDowned?.Invoke(_stats);

        // Team-wipe → game over. AllDead() is true only when EVERY registered
        // player is at 0 HP (all downed/dead). Mirror the core's proven path
        // (EnergyManager drives the game-over UI) AND notify the orchestrator
        // (run-state + save cleanup). Both are guarded/idempotent.
        if (PlayerRegistry.Instance.AllDead())
        {
            if (EnergyManager.Instance != null && !EnergyManager.Instance.IsGameOver())
                EnergyManager.Instance.TriggerGameOver();
            GameOrchestrator.Instance?.TriggerGameOver();
        }

        return true;
    }

    /// <summary>Revive this player at <paramref name="healthPercent"/> of max HP.</summary>
    public void Revive(float healthPercent)
    {
        if (!IsDowned) return;
        IsDowned = false;
        ReenableControl();

        // Heal to a fraction of max, then grant brief immunity. Reuses the same
        // TemporaryReviveImmunity component QuickReviveEffect uses, which
        // PlayerStats.TakeDamage already honours.
        float pct = Mathf.Clamp(healthPercent, 0.05f, 1f);
        if (_stats != null) _stats.SetHealthAndNotify(_stats.maxHealth * pct);

        if (reviveInvulnerabilitySeconds > 0f && GetComponent<ImmunityPhasesEffect>() == null)
        {
            var existing = GetComponent<TemporaryReviveImmunity>();
            if (existing != null) Destroy(existing);
            gameObject.AddComponent<TemporaryReviveImmunity>().Initialize(reviveInvulnerabilitySeconds);
        }

        OnRevived?.Invoke(_stats);
        AnyRevived?.Invoke(_stats);
    }


    // Clear the downed state WITHOUT healing or granting immunity. Used by the
    // wave-checkpoint rewind (Phase 7c), which sets the exact wave-start HP
    // itself right after this. No-op if not downed.

    public void RestoreControl()
    {
        if (!IsDowned) return;
        IsDowned = false;
        ReenableControl();
        OnRevived?.Invoke(_stats);
        AnyRevived?.Invoke(_stats);
    }

    // Shared un-down: re-enable colliders, control, and visual. Does NOT touch HP.
    private void ReenableControl()
    {
        for (int i = 0; i < _disabledColliders.Count; i++)
            if (_disabledColliders[i] != null) _disabledColliders[i].enabled = true;
        _disabledColliders.Clear();

        if (_aim != null) _aim.enabled = true;
        if (_placer != null) _placer.enabled = true;
        if (_attack != null) _attack.SetSuppressed(false);
        if (_input != null) _input.ActivateInput();

        if (_movement != null) _movement.ExitDownedVisual();
        else ApplyTint(false);
    }

    /// <summary>Convenience revive at the inspector-configured percentage.</summary>
    public void Revive() => Revive(reviveHealthPercent);

    private void ApplyTint(bool downed)
    {
        if (_sprite == null) return;
        _sprite.color = downed ? downedTint : _spriteBaseColor;
    }

    [ContextMenu("DEBUG / Revive now")]
    private void DebugRevive() => Revive(reviveHealthPercent);
}

