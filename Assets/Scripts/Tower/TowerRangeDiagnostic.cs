using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// =============================================================================
//  TOWER RANGE DIAGNOSTIC          drop on ANY active GameObject, press F9
// -----------------------------------------------------------------------------
//  Dumps every value in the tower targeting chain, in world units, so a mismatch
//  is visible instead of inferred. Nothing is modified — read-only.
//
//  THE CHAIN A TOWER MUST WALK TO FIRE:
//    1. OnTriggerEnter2D   — enemy collider overlaps rangeCollider (a CircleCollider2D
//                            whose WORLD radius is radius x lossyScale, NOT radius)
//    2. IsEnemy(other)     — tag/type check
//    3. targetLayer mask   — ((1 << enemy.layer) & targetLayer) != 0
//    4. IsValidTarget      — dist <= ProjectileRange           (world units)
//    5. Attack()           — dist <= meleeRange ? melee : dist <= ProjectileRange
//
//  Any one of those failing produces "the tower just doesn't shoot" with NO error.
//  The scale trap in step 1 is the sneaky one: set rangeCollider.radius = 5 on a
//  transform scaled 0.25 and the real trigger is 1.25 world units, while every
//  distance check downstream uses unscaled world distance.
// =============================================================================
public class TowerRangeDiagnostic : MonoBehaviour
{
    // NOTE: this project uses the new Input System package, so UnityEngine.Input
    // THROWS rather than returning false — an earlier version of this file used it and
    // produced an InvalidOperationException every frame while never reading the key.
    // The #if keeps the file usable in either project setup.
    [Tooltip("Also dump automatically N seconds after the first tower is placed. 0 = off.")]
    public float autoDumpAfterSeconds = 0f;

    [Tooltip("Only report on towers whose name contains this. Empty = all towers.")]
    public string filterTowerName = "";

    private const BindingFlags PRIV = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private bool _autoDumped;
    private float _timer;

    void Update()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null && kb.f9Key.wasPressedThisFrame) pressed = true;
#else
        if (Input.GetKeyDown(KeyCode.F9)) pressed = true;
#endif
        if (pressed) Dump();

        // Belt and braces: if the key still does not register for any reason, this fires
        // once on its own so you get a report regardless.
        if (!_autoDumped && autoDumpAfterSeconds > 0f)
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer >= autoDumpAfterSeconds &&
                FindObjectsByType<Tower>(FindObjectsSortMode.None).Length > 0)
            {
                _autoDumped = true;
                Dump();
            }
        }
    }

    void Awake()
    {
        Debug.Log("[TowerDiag] Ready. Press F9 with a tower placed and an enemy nearby " +
                  "(or set Auto Dump After Seconds on this component).");
    }

    [ContextMenu("Dump tower range report")]
    public void Dump()
    {
        var towers = FindObjectsByType<Tower>(FindObjectsSortMode.None)
                     .Where(t => string.IsNullOrEmpty(filterTowerName) ||
                                 t.towerName.ToLower().Contains(filterTowerName.ToLower()))
                     .ToArray();

        var enemies = FindObjectsByType<EnemyStats>(FindObjectsSortMode.None)
                      .Where(e => e != null).ToArray();

        var sb = new StringBuilder(4096);
        sb.AppendLine($"╔══ TOWER RANGE DIAGNOSTIC — {towers.Length} tower(s), {enemies.Length} enemy(ies) ══");

        if (towers.Length == 0)
        {
            sb.AppendLine("║ No towers found. Place one first.");
            Debug.Log(sb.ToString());
            return;
        }

        foreach (var t in towers)
        {
            sb.AppendLine("║");
            sb.AppendLine($"║ ── {t.towerName}  (type {t.towerType}, GO '{t.name}') ──");

            // ---- scale, the usual suspect for "world units don't match" ----
            Vector3 ls = t.transform.lossyScale;
            bool scaled = Mathf.Abs(ls.x - 1f) > 0.001f || Mathf.Abs(ls.y - 1f) > 0.001f;
            sb.AppendLine($"║   lossyScale        : ({ls.x:F3}, {ls.y:F3}){(scaled ? "   <<< NOT 1 — see trigger radius below" : "")}");

            // ---- ranges ----
            float projRange = t.ProjectileRange;
            sb.AppendLine($"║   range (public)    : {t.range:F2}");
            sb.AppendLine($"║   ProjectileRange   : {projRange:F2}{(projRange <= 0.01f ? "   <<< ZERO — nothing can ever be in range" : "")}");
            sb.AppendLine($"║   damage / fireRate : {t.damage:F1} / {t.fireRate:F2}");

            // ---- the trigger collider ----
            var cc = t.GetComponent<CircleCollider2D>();
            if (cc == null)
                sb.AppendLine("║   rangeCollider     : MISSING  <<< OnTriggerEnter2D can never fire");
            else
            {
                float worldR = cc.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y));
                sb.AppendLine($"║   collider radius   : {cc.radius:F2} local  ->  {worldR:F2} WORLD" +
                              $"   (isTrigger={cc.isTrigger}, enabled={cc.enabled})");
                if (!cc.isTrigger)
                    sb.AppendLine("║       <<< isTrigger is FALSE — OnTriggerEnter2D will never be called");
                if (worldR + 0.01f < projRange)
                    sb.AppendLine($"║       <<< MISMATCH: trigger reaches {worldR:F2} but ProjectileRange is {projRange:F2}." +
                                  "\n║           Enemies are only DETECTED inside the smaller radius.");
            }

            // ---- layers ----
            int mask = t.targetLayer.value;
            sb.AppendLine($"║   tower layer       : {t.gameObject.layer} '{LayerMask.LayerToName(t.gameObject.layer)}'");
            sb.AppendLine($"║   targetLayer mask  : {mask}  ({(mask == -1 ? "Everything" : MaskToNames(mask))})");

            // ---- private state, via reflection so this compiles regardless ----
            var inRange = GetPriv<System.Collections.Generic.List<GameObject>>(t, "enemiesInRange");
            var target = GetPriv<GameObject>(t, "currentTarget");
            sb.AppendLine($"║   enemiesInRange    : {(inRange == null ? "?" : inRange.Count.ToString())}" +
                          $"{(inRange != null && inRange.Count == 0 && enemies.Length > 0 ? "   <<< EMPTY while enemies exist — detection is failing (step 1-3)" : "")}");
            sb.AppendLine($"║   currentTarget     : {(target == null ? "null" : target.name)}");
            sb.AppendLine($"║   firePoint         : {(t.FirePoint == null ? "NULL  <<< InitializeTentacles() never ran" : "ok")}");

            // ---- per-enemy breakdown: exactly why each one is or isn't valid ----
            if (enemies.Length == 0) sb.AppendLine("║   (no enemies alive to test against)");
            foreach (var e in enemies.OrderBy(e => Vector2.Distance(t.transform.position, e.transform.position)).Take(4))
            {
                float d = Vector2.Distance(t.transform.position, e.transform.position);
                bool layerOk = ((1 << e.gameObject.layer) & mask) != 0;
                bool rangeOk = d <= projRange;
                var ecol = e.GetComponent<Collider2D>();

                sb.AppendLine($"║     enemy '{e.name}'  dist {d:F2}");
                sb.AppendLine($"║        layer {e.gameObject.layer} '{LayerMask.LayerToName(e.gameObject.layer)}'" +
                              $"  maskMatch={(layerOk ? "YES" : "NO   <<< targetLayer excludes this enemy")}");
                sb.AppendLine($"║        collider={(ecol == null ? "NONE  <<< cannot trigger" : ecol.GetType().Name + (ecol.enabled ? "" : " (DISABLED)"))}" +
                              $"  inRangeList={(inRange != null && inRange.Contains(e.gameObject) ? "yes" : "NO")}");
                sb.AppendLine($"║        dist<=ProjectileRange : {(rangeOk ? "YES" : $"NO   ({d:F2} > {projRange:F2})")}");

                if (ecol != null && Physics2D.GetIgnoreLayerCollision(t.gameObject.layer, e.gameObject.layer))
                    sb.AppendLine($"║        <<< Physics2D LAYER MATRIX ignores " +
                                  $"'{LayerMask.LayerToName(t.gameObject.layer)}' vs " +
                                  $"'{LayerMask.LayerToName(e.gameObject.layer)}' — " +
                                  "OnTriggerEnter2D can NEVER fire for this pair. " +
                                  "Edit ▸ Project Settings ▸ Physics 2D ▸ Layer Collision Matrix.");
            }
        }

        sb.AppendLine("╚══════════════════════════════════════════════════════");
        Debug.Log(sb.ToString());
    }

    private static string MaskToNames(int mask)
    {
        var names = Enumerable.Range(0, 32)
            .Where(i => (mask & (1 << i)) != 0)
            .Select(i => LayerMask.LayerToName(i))
            .Where(n => !string.IsNullOrEmpty(n));
        return string.Join(", ", names);
    }

    private static T GetPriv<T>(object obj, string field) where T : class
    {
        var f = obj.GetType().GetField(field, PRIV);
        return f?.GetValue(obj) as T;
    }
}


