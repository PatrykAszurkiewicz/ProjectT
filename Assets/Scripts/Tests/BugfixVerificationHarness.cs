using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Verifies the enemy-mechanics bug fixes at RUNTIME, against live enemies.
// Drop on any active GameObject in a gameplay scene, press Play, spawn some enemies,
// then hit the hotkeys. Every line is tagged [FIXTEST]; filter the Console by it.
//
//   F5 = run every test in sequence
//   F6 = T1  knockback duration
//   F7 = T2  damage-flash latch  +  T3  flash preserves an external tint
//   F9 = T4  parry-stun release (Bomber / Buffer)
//
// v5 — T1 reads EnemyController.IsKnockedBack instead of guessing from velocity.
// v1 reported false FAILs for T2/T3 in a live scene: it grabbed an arbitrary enemy,
// hit it, waited a fixed 1s and sampled the colour once. In a real fight that enemy
// is ALSO being shot by towers (restarting the flash, so the single sample caught it
// mid-blink at damageFlashColor) and flipped by SmoothSpriteFlip (which writes
// spriteRenderer.color on every direction change, so the T3 tint was overwritten with
// the sprite's own colour). Same build passed and failed run to run.
//
// v2 fixes that three ways: it silences the known colour writer on the test subject
// for the duration, it watches currentHealth so any damage that is NOT ours is
// detected, and it POLLS for the expected colour instead of sampling once. A subject
// that is genuinely contended now reports SKIP, and only a colour that is truly stuck
// on the flash value reports FAIL.
//
// v3 applies the same lesson to T1. EnemyController.FixedUpdate zeroes linearVelocity
// and RETURNS on four gates that all sit BEFORE the knockback branch — frozen,
// parry-stunned, game-over, boss-laser. A subject in any of those states shows zero
// velocity on the very first physics step, which v2 read as "knockback lasted 0.02s"
// and reported as FAIL. In practice this hit any enemy the player had just parried or
// frozen — a meleeing Brute, most often. v3 requires the knockback to visibly ENGAGE
// (a non-zero velocity sample) before it will time anything, and walks to another
// enemy when it doesn't.
//
// v4 fixes how T1 SAMPLES. v3 polled with `yield return new WaitForFixedUpdate()`,
// which resumes once per PHYSICS LOOP, not once per physics STEP. Under load — and
// six harnesses in one editor scene is load — Unity runs several FixedUpdate steps to
// catch up and the coroutine only wakes at the end, so the single frame where
// EnemyController writes linearVelocity = zero can be stepped clean over. The clock
// then kept running until the enemy happened to stop again for an unrelated reason
// (reaching attack range), reporting 1.94s for a 0.4s knockback. A real MonoBehaviour
// FixedUpdate runs exactly once per step and cannot miss it, so the sampling moved
// there. v4 also treats an OVER-measurement as inconclusive rather than a failure:
// the bug under test makes knockback too SHORT, so only a short reading is diagnostic.

// NOTE ON SIDE EFFECTS: this harness perturbs live enemies on purpose — T1 knocks one
// across the map, T4 applies a real ParryStunEffect, T2/T3 tint a sprite and deal 0.01
// damage. Seeing an enemy stunned or flung after pressing F5 is the test working. Run
// it on a throwaway wave.
public class BugfixVerificationHarness : MonoBehaviour
{
    private const string TAG = "[FIXTEST] ";

    [Tooltip("Knockback duration T1 asks for. The fix makes the measured time match " +
             "this; before the fix it came back at roughly HALF. Expect one extra " +
             "physics step of measurement overhead (~0.02s) — that is the poll, not a bug.")]
    public float knockbackTestDuration = 0.4f;

    [Tooltip("Tolerance for T1, as a fraction of the requested duration.")]
    [Range(0.05f, 0.5f)] public float knockbackTolerance = 0.25f;

    [Tooltip("How many different enemies T2/T3 will try before giving up. A busy " +
             "scene can have several enemies under fire at once.")]
    [Range(1, 6)] public int colourTestCandidates = 3;

    private bool _running;

    private void Awake()
    {
        Debug.LogWarning(TAG + "harness v5 ready. F5 = run all, F6 = knockback, " +
                         "F7 = damage flash, F9 = parry-stun release. " +
                         "Spawn some enemies first.");
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        bool all = kb.f5Key.wasPressedThisFrame;
        bool kbk = kb.f6Key.wasPressedThisFrame;
        bool flash = kb.f7Key.wasPressedThisFrame;
        bool stun = kb.f9Key.wasPressedThisFrame;
#else
        bool all   = Input.GetKeyDown(KeyCode.F5);
        bool kbk   = Input.GetKeyDown(KeyCode.F6);
        bool flash = Input.GetKeyDown(KeyCode.F7);
        bool stun  = Input.GetKeyDown(KeyCode.F9);
#endif
        if (_running) return;
        if (all) StartCoroutine(RunAll());
        else if (kbk) StartCoroutine(Wrap(T1_KnockbackDuration()));
        else if (flash) StartCoroutine(Wrap(T2_T3_DamageFlash()));
        else if (stun) StartCoroutine(Wrap(T4_ParryStunRelease()));
    }

    private IEnumerator Wrap(IEnumerator test)
    {
        _running = true;
        yield return test;
        _running = false;
    }

    private IEnumerator RunAll()
    {
        _running = true;
        L("================ BUGFIX VERIFICATION (v5) ================");
        yield return T1_KnockbackDuration();
        yield return T2_T3_DamageFlash();
        yield return T4_ParryStunRelease();
        L("=========================================================");
        _running = false;
    }

    // T1 ─ Knockback was decremented in BOTH Update and FixedUpdate, so it drained at
    // ~2x real time. EnemyController zeroes linearVelocity EXACTLY when the timer
    // expires and runs no other movement code while knocked back, so "first frame
    // velocity is exactly zero" is a clean end marker.
    // Knockback sampling state, driven from FixedUpdate (see the v4 note in the header).
    private EnemyController _kbEc;
    private Rigidbody2D _kbRb;
    private bool _kbSampling, _kbEngaged, _kbFinished, _kbTimedOut, _kbLost;
    private float _kbElapsed;

    private void FixedUpdate()
    {
        if (!_kbSampling) return;

        if (_kbEc == null || _kbRb == null)
        { _kbSampling = false; _kbLost = true; _kbFinished = true; return; }

        _kbElapsed += Time.fixedDeltaTime;

        // Ask the controller directly rather than inferring from velocity. Velocity
        // hits zero the instant the body collides with a wall or obstacle, which is
        // indistinguishable from "the knockback expired" — that is what produced the
        // 0.040s and 0.120s false FAILs: the subject was flung into scenery.
        bool moving = _kbEc.IsKnockedBack;

        if (!_kbEngaged)
        {
            if (moving) { _kbEngaged = true; return; }

            // Never entered the knockback state at all. ApplyKnockback refuses when
            // the controller is disabled, and the frozen / parry-stunned / game-over /
            // boss-laser gates all sit BEFORE the knockback branch, so this is
            // "knockback refused", not "finished instantly".
            if (_kbElapsed >= 0.15f) { _kbSampling = false; _kbFinished = true; }
            return;
        }

        // Engaged, then the controller cleared the flag: that IS the end, exactly.
        if (!moving) { _kbSampling = false; _kbFinished = true; return; }

        if (_kbElapsed >= 3f) { _kbSampling = false; _kbTimedOut = true; _kbFinished = true; }
    }

    private IEnumerator T1_KnockbackDuration()
    {
        var tried = new List<EnemyController>();

        for (int attempt = 0; attempt < colourTestCandidates; attempt++)
        {
            var ec = FindKnockbackSubject(tried);
            if (ec == null)
            {
                Skip("T1 knockback", tried.Count == 0
                    ? "no live enemy with a dynamic Rigidbody2D — spawn a wave first."
                    : $"all {tried.Count} candidate(s) were inconclusive (refused the knockback, " +
                      "or died / were interfered with mid-measurement).");
                yield break;
            }
            tried.Add(ec);
            L($"  T1 subject: '{ec.name}' (attempt {attempt + 1}/{colourTestCandidates})");

            bool conclusive = false;
            yield return MeasureKnockback(ec, r => conclusive = r);
            if (conclusive) yield break;
        }
    }

    private IEnumerator MeasureKnockback(EnemyController ec, System.Action<bool> report)
    {
        _kbEc = ec;
        _kbRb = ec.GetComponent<Rigidbody2D>();
        _kbElapsed = 0f;
        _kbEngaged = _kbFinished = _kbTimedOut = _kbLost = false;

        ec.ApplyKnockback(Vector2.right, 8f, knockbackTestDuration);
        _kbSampling = true;                       // FixedUpdate takes over from here

        while (!_kbFinished) yield return null;
        _kbSampling = false;

        float t = _kbElapsed;
        string who = ec != null ? ec.name : "(destroyed)";

        if (_kbLost)
        { Skip("T1 knockback", $"'{who}' died mid-measurement — trying another."); report(false); yield break; }

        if (!_kbEngaged)
        {
            Skip("T1 knockback", $"'{who}' never moved — it is frozen, parry-stunned, or the run " +
                                  "is over, so FixedUpdate zeroes its velocity before the knockback " +
                                  "branch. Trying another enemy.");
            report(false); yield break;
        }

        if (_kbTimedOut)
        { Skip("T1 knockback", $"'{who}' never came to rest within 3s — trying another."); report(false); yield break; }

        float expected = knockbackTestDuration;
        float slack = Mathf.Max(expected * knockbackTolerance, Time.fixedDeltaTime * 2f);

        if (Mathf.Abs(t - expected) <= slack)
        {
            Pass("T1 knockback", $"held {t:F3}s for a requested {expected:F3}s " +
                                 $"(up to one {Time.fixedDeltaTime:F3}s step of sampling offset is expected).");
            L("  NOTE: knockback now lasts its FULL configured duration. Anything tuned " +
              "against the old halved value travels ~2x farther now — check " +
              "SplitterController.burstDuration and VortexSpawner.birthPushDuration.");
            report(true); yield break;
        }

        if (t < expected * 0.75f)
        {
            // The ONLY diagnostic direction: the bug under test makes knockback SHORTER.
            Fail("T1 knockback", $"held only {t:F3}s of {expected:F3}s, and the body DID move first " +
                                 "(so it is not a frozen/stunned subject) — the double-decrement is " +
                                 "still present. Is the OLD EnemyController.cs still in the project?");
            report(true); yield break;
        }

        // Over-measured. The fix cannot cause this, so it is not a failure — the subject
        // resumed moving and only came to rest later (walked into attack range, hit a
        // wall). Try a quieter enemy instead of crying wolf.
        Skip("T1 knockback", $"'{who}' measured {t:F3}s for a requested {expected:F3}s. Over-measuring " +
                              "cannot be caused by the bug under test (which SHORTENS knockback); the " +
                              "subject most likely stopped later for its own reasons. Trying another.");
        report(false);
    }

    // T2 ─ The flash coroutine used to re-read spriteRenderer.color on every run. Two
    // hits inside one blink made it capture the FLASH colour as the restore target and
    // the enemy latched bright white forever.
    // T3 ─ The restore target must still be whatever tint was on the sprite BEFORE the
    // flash (freeze cyan, confusion magenta), not a fixed spawn-time snapshot.
    private IEnumerator T2_T3_DamageFlash()
    {
        var tried = new List<EnemyStats>();

        for (int attempt = 0; attempt < colourTestCandidates; attempt++)
        {
            var stats = FindColourTestSubject(tried, out SpriteRenderer sr);
            if (stats == null)
            {
                Skip("T2/T3 damage flash", tried.Count == 0
                    ? "no quiet enemy with a SpriteRenderer (all are boss / confused / berserk / stunned)."
                    : $"every one of the {tried.Count} candidate(s) was contended. " +
                      "Re-run between waves, or press F7 with a lull in the fighting.");
                yield break;
            }
            tried.Add(stats);
            L($"  T2/T3 subject: '{stats.name}' (attempt {attempt + 1}/{colourTestCandidates})");

            // Silence the known competing colour writer for the duration. SmoothSpriteFlip
            // repaints the sprite on every direction change (BomberController disables it
            // the same way while its fuse blink owns the colour).
            Behaviour flip = stats.GetComponent<SmoothSpriteFlip>() as Behaviour;
            bool flipWasEnabled = flip != null && flip.enabled;
            if (flipWasEnabled) flip.enabled = false;

            bool done = false;
            yield return RunColourTest(stats, sr, r => done = r);

            if (flip != null && flipWasEnabled) flip.enabled = true;
            if (done) yield break;   // conclusive (PASS or FAIL) — stop here
        }
    }

    // Returns true through `report` when the subject gave a conclusive answer.
    private IEnumerator RunColourTest(EnemyStats stats, SpriteRenderer sr, System.Action<bool> report)
    {
        Color rest = sr.color;
        Color flashCol = stats.damageFlashColor;

        // Baseline: the colour must be its own for a moment before we trust anything.
        for (int i = 0; i < 12; i++)
        {
            yield return null;
            if (stats == null || sr == null) { Skip("T2/T3 damage flash", "enemy died before the test began."); report(false); yield break; }
            if (!ColorsClose(sr.color, rest))
            {
                Skip("T2/T3 damage flash", $"'{stats.name}' colour is already being driven by " +
                                           "something else — trying another enemy.");
                report(false); yield break;
            }
        }

        // ---- T2: rapid double hit, the second landing MID-BLINK while the sprite is lit.
        float hpBefore = stats.currentHealth;
        const float probe = 0.01f;

        stats.TakeDamage(probe);
        yield return new WaitForSeconds(0.05f);
        if (stats == null || sr == null) { Skip("T2/T3 damage flash", "enemy died mid-test."); report(false); yield break; }
        stats.TakeDamage(probe);

        var verdict = new ColourVerdict();
        yield return SettleTo(stats, sr, rest, flashCol, hpBefore, probe * 2f, verdict);

        if (verdict.outcome == Outcome.Contended)
        {
            Skip("T2 flash latch", verdict.detail + " — trying another enemy.");
            report(false); yield break;
        }
        if (verdict.outcome == Outcome.Settled)
            Pass("T2 flash latch", "sprite returned to its pre-hit colour after a rapid double hit.");
        else
            Fail("T2 flash latch", $"sprite stayed pinned at the flash colour {sr.color} " +
                                   $"(resting was {rest}) — it latched. Old EnemyStats.cs still in the project?");

        // ---- T3: external tint, hit through it, tint must survive.
        Color tint = new Color(0.2f, 0.9f, 1f, sr.color.a);   // stand-in for the freeze cyan
        sr.color = tint;
        yield return null;

        hpBefore = stats.currentHealth;
        stats.TakeDamage(probe);

        var v2 = new ColourVerdict();
        yield return SettleTo(stats, sr, tint, flashCol, hpBefore, probe, v2);

        if (v2.outcome == Outcome.Contended)
            Skip("T3 tint preserved", v2.detail);
        else if (v2.outcome == Outcome.Settled)
            Pass("T3 tint preserved", "external tint survived a damage flash.");
        else
            Fail("T3 tint preserved", $"tint {tint} was replaced by {sr.color} — a hit taken while " +
                                       "frozen/confused strips the tint.");

        if (sr != null) sr.color = rest;
        report(true);
    }

    private enum Outcome { Settled, Stuck, Contended }
    private class ColourVerdict { public Outcome outcome; public string detail; }

    // Polls for `want`, rather than sampling once at a fixed delay. Distinguishes:
    //   Settled   — reached `want` and HELD it (the fix works)
    //   Stuck     — pinned on the flash colour and never left (the real bug)
    //   Contended — took damage we did not deal, or the colour kept moving without
    //               ever settling (someone else owns the sprite; not a verdict)
    private IEnumerator SettleTo(EnemyStats stats, SpriteRenderer sr, Color want, Color flashCol,
                                 float hpBefore, float ourDamage, ColourVerdict verdict)
    {
        const float timeout = 3f;
        const float holdFor = 0.25f;
        float t = 0f, held = 0f;
        bool everLeftFlash = false;

        while (t < timeout)
        {
            yield return null;
            float dt = Time.deltaTime;
            t += dt;

            if (stats == null || sr == null)
            { verdict.outcome = Outcome.Contended; verdict.detail = "enemy died mid-test"; yield break; }

            // Health moved by more than we applied => somebody else is hitting it, which
            // restarts the flash and makes any colour reading meaningless.
            float lost = hpBefore - stats.currentHealth;
            if (lost > ourDamage + 0.001f)
            {
                verdict.outcome = Outcome.Contended;
                verdict.detail = $"'{stats.name}' took {lost - ourDamage:F2} damage from something " +
                                 "other than this test (towers/player are shooting it)";
                yield break;
            }

            if (!ColorsClose(sr.color, flashCol)) everLeftFlash = true;

            if (ColorsClose(sr.color, want))
            {
                held += dt;
                if (held >= holdFor) { verdict.outcome = Outcome.Settled; yield break; }
            }
            else held = 0f;
        }

        // Never settled. Pinned on the flash colour the whole time = the genuine bug.
        // Anything else moving around = something else owns this sprite.
        verdict.outcome = everLeftFlash ? Outcome.Contended : Outcome.Stuck;
        if (verdict.outcome == Outcome.Contended)
            verdict.detail = $"'{stats.name}' colour never settled and is not stuck on the flash " +
                             "value — another system is writing it";
    }

    // T4 ─ BomberController tested for the PRESENCE of a ParryStunEffect rather than
    // IsStunActive. Powerful Parry leaves the component alive after the freeze window
    // as a damage debuff, so the Bomber stayed pinned forever. BufferController had no
    // parry-stun gate at all.
    private IEnumerator T4_ParryStunRelease()
    {
        yield return StunAndCheckResumes<BomberController>("T4 Bomber");
        yield return StunAndCheckResumes<BufferController>("T4 Buffer");
    }

    private IEnumerator StunAndCheckResumes<T>(string label) where T : MonoBehaviour
    {
        T ctrl = null;
        foreach (var c in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
        {
            var es = c != null ? c.GetComponent<EnemyStats>() : null;
            if (es != null && !es.IsDead() && c.isActiveAndEnabled) { ctrl = c; break; }
        }
        if (ctrl == null) { Skip(label, $"no live {typeof(T).Name} in scene."); yield break; }

        var rb = ctrl.GetComponent<Rigidbody2D>();
        if (rb == null) { Skip(label, "no Rigidbody2D."); yield break; }

        try { ParryStunEffect.ApplyOrRefresh(ctrl.gameObject, 0); }
        catch (System.Exception e) { Skip(label, "ParryStunEffect.ApplyOrRefresh threw: " + e.Message); yield break; }

        var stun = ctrl.GetComponent<ParryStunEffect>();
        if (stun == null) { Skip(label, "no ParryStunEffect after ApplyOrRefresh."); yield break; }

        float t = 0f;
        while (t < 8f)
        {
            yield return new WaitForSeconds(0.1f);
            t += 0.1f;
            if (ctrl == null) { Skip(label, "enemy died mid-test."); yield break; }
            bool active;
            try { active = stun != null && stun.IsStunActive; } catch { break; }
            if (!active) break;
        }

        if (ctrl == null) { Skip(label, "enemy died mid-test."); yield break; }

        Vector2 from = rb.position;
        yield return new WaitForSeconds(0.6f);
        if (ctrl == null) { Skip(label, "enemy died mid-test."); yield break; }

        float moved = Vector2.Distance(rb.position, from);
        bool componentStillThere = ctrl.GetComponent<ParryStunEffect>() != null;

        if (moved > 0.02f)
            Pass(label, $"resumed moving after the stun lapsed ({moved:F3} units" +
                        (componentStillThere ? ", with the ParryStunEffect component still attached — " +
                         "exactly the case that used to pin it" : "") + ").");
        else if (componentStillThere)
            Fail(label, "still not moving while a lapsed ParryStunEffect is attached — the " +
                        "presence-vs-IsStunActive bug is still present.");
        else
            Skip(label, "did not move, but no stun component remains — likely idle for another " +
                        "reason (no target, armed fuse, game over).");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // A subject that can actually BE knocked back: dynamic body, not a boss, not
    // already parry-stunned, and not one we have tried.
    private static EnemyController FindKnockbackSubject(List<EnemyController> exclude)
    {
        foreach (var ec in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
        {
            if (ec == null || !ec.isActiveAndEnabled) continue;
            if (exclude.Contains(ec)) continue;

            var es = ec.GetComponent<EnemyStats>();
            if (es == null || es.IsDead()) continue;
            if (es is BaseBossStats) continue;          // don't shove a boss around

            var rb = ec.GetComponent<Rigidbody2D>();
            if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) continue;

            // Cheap pre-filter; MeasureKnockback still verifies the push engages.
            var stun = ec.GetComponent<ParryStunEffect>();
            if (stun != null) continue;

            return ec;
        }
        return null;
    }

    // A subject whose sprite colour nobody else already owns: not a boss, not confused,
    // not berserked, not parry-stunned, and not one we have already tried.
    private static EnemyStats FindColourTestSubject(List<EnemyStats> exclude, out SpriteRenderer sr)
    {
        foreach (var es in Object.FindObjectsByType<EnemyStats>(FindObjectsSortMode.None))
        {
            if (es == null || es.IsDead() || !es.isActiveAndEnabled) continue;
            if (es is BaseBossStats) continue;
            if (exclude.Contains(es)) continue;
            if (es.GetComponent<ConfusedEnemy>() != null) continue;
            if (es.GetComponent<BerserkEnemy>() != null) continue;
            if (es.GetComponent<ParryStunEffect>() != null) continue;
            if (es.GetComponent<GremlinController>() != null) continue;   // drives its own colour
            var r = es.GetComponent<SpriteRenderer>();
            if (r != null && r.sprite != null) { sr = r; return es; }
        }
        sr = null;
        return null;
    }

    private static bool ColorsClose(Color a, Color b)
        => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f
        && Mathf.Abs(a.b - b.b) < 0.02f && Mathf.Abs(a.a - b.a) < 0.02f;

    private static void L(string m) => Debug.Log(TAG + m);
    private static void Pass(string test, string detail) => Debug.Log(TAG + $"PASS  {test} — {detail}");
    private static void Fail(string test, string detail) => Debug.LogError(TAG + $"FAIL  {test} — {detail}");
    private static void Skip(string test, string detail) => Debug.LogWarning(TAG + $"SKIP  {test} — {detail}");
}





