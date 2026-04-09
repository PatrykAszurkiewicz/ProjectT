using UnityEngine;


// PARRY CALIBRATION OVERLAY
// Add to enemy prefab. Shows large readable debug info above enemy.
// Reads attackHitFrame, parryFrameStart, parryFrameEnd from EnemyController.

public class EnemyParryDebug : MonoBehaviour
{
    private SpriteRenderer enemySR;
    private EnemyStats enemyStats;
    private EnemyAnimationController animController;
    private EnemyController enemyController;

    private GameObject container;
    private TextMesh line1;  // sprite name + state
    private TextMesh line2;  // frame config summary
    private TextMesh line3;  // live phase during attack
    private TextMesh line4;  // instructions / parry result

    // Bar elements
    private SpriteRenderer barBg;
    private SpriteRenderer barParryZone;
    private SpriteRenderer barHitMarker;
    private SpriteRenderer barCursor;

    // Per-frame tick labels
    private TextMesh[] ticks;
    private SpriteRenderer[] tickBgs; // small colored squares behind each tick

    private const float BAR_Y = 2.2f;
    private const float BAR_W = 3.0f;
    private const float BAR_H = 0.15f;

    private string lastSprite = "";
    private bool wasAttacking;
    private float atkStartTime = -1f;

    // Read from EnemyController
    private int hitFrame;
    private int parryStart;
    private int parryEnd;

    private static Sprite _px;


    void Start()
    {
        enemySR = GetComponent<SpriteRenderer>();
        enemyStats = GetComponent<EnemyStats>();
        animController = GetComponent<EnemyAnimationController>();
        enemyController = GetComponent<EnemyController>();

        if (enemySR == null || enemyStats == null || enemyStats.enemyData == null)
        { enabled = false; return; }

        ReadFields();
        Build();
    }

    void Update()
    {
        if (container == null) return;

        float s = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, 0.01f);
        container.transform.rotation = Quaternion.identity;
        container.transform.localScale = Vector3.one / s;

        UpdateLine1();
        UpdateTimeline();
    }

    //  READ FIELDS

    private void ReadFields()
    {
        hitFrame = 0;
        parryStart = 0;
        parryEnd = 0;

        if (enemyController == null) return;

        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var hf = typeof(EnemyController).GetField("attackHitFrame", flags);
        var ps = typeof(EnemyController).GetField("parryFrameStart", flags);
        var pe = typeof(EnemyController).GetField("parryFrameEnd", flags);

        if (hf != null) hitFrame = Mathf.Max((int)hf.GetValue(enemyController), 0);
        if (ps != null) parryStart = Mathf.Max((int)ps.GetValue(enemyController), 0);
        if (pe != null) parryEnd = Mathf.Max((int)pe.GetValue(enemyController), 0);

        // Ensure parryEnd >= parryStart
        if (parryEnd < parryStart) parryEnd = parryStart;
    }

    //  BUILD

    private void Build()
    {
        float s = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, 0.01f);
        container = new GameObject("__ParryDebug__");
        container.transform.SetParent(transform, false);
        container.transform.localScale = Vector3.one / s;

        var ed = enemyStats.enemyData;
        int atkS = ed.attack.startFrame;
        int atkE = atkS + ed.attack.frameCount - 1;
        int fc = ed.attack.frameCount;
        float half = BAR_W * 0.5f;

        // Absolute sprite indices
        int hitAbs = atkS + hitFrame;
        int parryStartAbs = atkS + parryStart;
        int parryEndAbs = atkS + parryEnd;

        // ── TEXT LINES ──
        line1 = Txt("L1", new Vector3(0, BAR_Y + 0.85f, 0), Color.cyan);
        line1.text = "...";

        line2 = Txt("L2", new Vector3(0, BAR_Y + 0.6f, 0), Color.yellow);
        line2.text = $"ATK sprites {atkS}-{atkE} ({fc}f @ {ed.animationSpeed:F2}s)   " +
                     $"HIT=sprite {hitAbs}   PARRY={parryStartAbs}-{parryEndAbs}";

        line3 = Txt("L3", new Vector3(0, BAR_Y + 0.38f, 0), Color.white);
        line3.text = "";

        line4 = Txt("L4", new Vector3(0, BAR_Y + 0.2f, 0), new Color(0.8f, 0.8f, 0.8f));
        line4.fontSize = 60;
        if (parryStart == 0 && parryEnd == 0 && hitFrame == 0)
            line4.text = "SET attackHitFrame + parryFrameStart/End on EnemyController!";
        else
            line4.text = $"Parry window = {(parryEnd - parryStart + 1) * ed.animationSpeed:F2}s " +
                         $"({parryEnd - parryStart + 1} frames)";

        //  BAR BACKGROUND 
        barBg = Bar("BG", new Vector3(0, BAR_Y, 0),
            new Vector3(BAR_W, BAR_H, 1), new Color(0.2f, 0.2f, 0.2f, 0.9f), 9800);

        //  PARRY ZONE (blue) 
        if (fc > 0 && (parryStart > 0 || parryEnd > 0 || hitFrame > 0))
        {
            float t0 = (float)parryStart / fc;
            float t1 = (float)(parryEnd + 1) / fc;
            t1 = Mathf.Min(t1, 1f);
            float w = (t1 - t0) * BAR_W;
            float cx = -half + (t0 + t1) * 0.5f * BAR_W;

            barParryZone = Bar("Parry", new Vector3(cx, BAR_Y, 0),
                new Vector3(w, BAR_H * 0.9f, 1),
                new Color(0.15f, 0.45f, 1f, 0.85f), 9801);
        }

        //  HIT MARKER (red) 
        if (fc > 0)
        {
            float ht = ((float)hitFrame + 0.5f) / fc;
            barHitMarker = Bar("Hit", new Vector3(-half + ht * BAR_W, BAR_Y, 0),
                new Vector3(0.05f, BAR_H * 1.8f, 1), Color.red, 9803);
        }

        //  CURSOR 
        barCursor = Bar("Cur", new Vector3(-half, BAR_Y, 0),
            new Vector3(0.04f, BAR_H * 1.5f, 1), Color.white, 9804);
        barCursor.enabled = false;

        //  FRAME TICKS 
        if (fc > 0 && fc <= 30)
        {
            ticks = new TextMesh[fc];
            tickBgs = new SpriteRenderer[fc];

            for (int i = 0; i < fc; i++)
            {
                float ft = ((float)i + 0.5f) / fc;
                float x = -half + ft * BAR_W;
                int absIdx = atkS + i;

                // Small background square per frame slot
                float slotW = BAR_W / fc;
                Color slotColor;
                if (i >= parryStart && i <= parryEnd)
                    slotColor = new Color(0.1f, 0.3f, 0.7f, 0.5f);
                else
                    slotColor = new Color(0.15f, 0.15f, 0.15f, 0.3f);
                if (i == hitFrame)
                    slotColor = new Color(0.7f, 0.1f, 0.1f, 0.5f);

                tickBgs[i] = Bar($"TB{i}", new Vector3(x, BAR_Y, 0),
                    new Vector3(slotW * 0.9f, BAR_H * 0.85f, 1), slotColor, 9799);

                // Label
                Color tc = Color.gray;
                if (i >= parryStart && i <= parryEnd) tc = new Color(0.5f, 0.8f, 1f);
                if (i == hitFrame) tc = Color.red;

                var tm = Txt($"T{i}", new Vector3(x, BAR_Y - 0.15f, 0), tc);
                tm.fontSize = 50;
                tm.characterSize = 0.025f;
                tm.text = absIdx.ToString();
                ticks[i] = tm;
            }
        }
    }

    //  UPDATE

    private void UpdateLine1()
    {
        if (line1 == null) return;
        string n = (enemySR.sprite != null) ? enemySR.sprite.name : "?";
        if (n == lastSprite) return;
        lastSprite = n;

        string st = "IDLE";
        if (animController != null)
        {
            if (animController.IsPlayingMeleeAttack()) st = "ATTACK";
            else if (animController.IsPlayingLaserAttack()) st = "LASER";
        }
        line1.text = $"{n}   [{st}]";
    }

    private void UpdateTimeline()
    {
        bool atk = animController != null && animController.IsPlayingMeleeAttack();
        var ed = enemyStats.enemyData;
        float half = BAR_W * 0.5f;
        int fc = ed.attack.frameCount;

        if (atk && !wasAttacking)
        {
            atkStartTime = Time.time;
            ReadFields();
        }
        wasAttacking = atk;

        if (!atk)
        {
            barCursor.enabled = false;
            line3.text = "idle";
            line3.color = new Color(0.5f, 0.5f, 0.5f);
            DimTicks(0.5f);
            return;
        }

        barCursor.enabled = true;
        float elapsed = Time.time - atkStartTime;
        float totalDur = ed.animationSpeed * fc;
        if (totalDur <= 0) return;

        float t = Mathf.Clamp01(elapsed / totalDur);
        float cf = elapsed / ed.animationSpeed;
        int cfi = Mathf.FloorToInt(cf);

        barCursor.transform.localPosition = new Vector3(-half + t * BAR_W, BAR_Y, 0);

        // Phase
        bool inParry = cfi >= parryStart && cfi <= parryEnd;
        bool atHit = Mathf.Abs(cf - hitFrame) < 0.5f;

        if (atHit)
        {
            line3.text = $">>> DAMAGE <<<   frame {cf:F1} / {fc}";
            line3.color = Color.red;
            barCursor.color = Color.red;
        }
        else if (inParry)
        {
            line3.text = $"PARRY WINDOW   frame {cf:F1} / {fc}   press RMB!";
            line3.color = new Color(0.3f, 0.7f, 1f);
            barCursor.color = new Color(0.3f, 0.7f, 1f);
        }
        else if (cf < parryStart)
        {
            line3.text = $"wind-up (too early)   frame {cf:F1} / {fc}";
            line3.color = new Color(0.6f, 0.6f, 0.6f);
            barCursor.color = Color.white;
        }
        else
        {
            line3.text = $"recovery (too late)   frame {cf:F1} / {fc}";
            line3.color = new Color(0.6f, 0.6f, 0.6f);
            barCursor.color = Color.white;
        }

        // Highlight ticks
        if (ticks != null)
        {
            for (int i = 0; i < ticks.Length; i++)
            {
                if (ticks[i] == null) continue;

                bool isCurrent = i == cfi;
                bool isPF = i >= parryStart && i <= parryEnd;
                bool isHF = i == hitFrame;

                if (isCurrent)
                {
                    ticks[i].color = Color.white;
                    ticks[i].characterSize = 0.032f;
                }
                else
                {
                    ticks[i].characterSize = 0.025f;
                    ticks[i].color = isHF ? Color.red
                        : isPF ? new Color(0.5f, 0.8f, 1f)
                        : new Color(0.5f, 0.5f, 0.5f);
                }

                // Also pulse the tick background
                if (tickBgs != null && tickBgs[i] != null)
                {
                    Color bg;
                    if (isCurrent)
                        bg = isHF ? new Color(1f, 0.2f, 0.2f, 0.8f)
                           : isPF ? new Color(0.2f, 0.5f, 1f, 0.8f)
                           : new Color(0.5f, 0.5f, 0.5f, 0.6f);
                    else if (isHF)
                        bg = new Color(0.7f, 0.1f, 0.1f, 0.5f);
                    else if (isPF)
                        bg = new Color(0.1f, 0.3f, 0.7f, 0.5f);
                    else
                        bg = new Color(0.15f, 0.15f, 0.15f, 0.3f);
                    tickBgs[i].color = bg;
                }
            }
        }
    }

    private void DimTicks(float a)
    {
        if (ticks == null) return;
        for (int i = 0; i < ticks.Length; i++)
        {
            if (ticks[i] != null) { Color c = ticks[i].color; c.a = a; ticks[i].color = c; }
            if (tickBgs != null && tickBgs[i] != null)
            { Color c = tickBgs[i].color; c.a = a * 0.5f; tickBgs[i].color = c; }
        }
    }

    //  HELPERS

    private TextMesh Txt(string name, Vector3 pos, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(container.transform, false);
        go.transform.localPosition = pos;

        var tm = go.AddComponent<TextMesh>();
        tm.alignment = TextAlignment.Center;
        tm.anchor = TextAnchor.LowerCenter;
        tm.characterSize = 0.045f;
        tm.fontSize = 70;
        tm.fontStyle = FontStyle.Bold;
        tm.color = color;
        tm.text = "";

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) { mr.sortingOrder = 9900; mr.sortingLayerName = "Default"; }
        return tm;
    }

    private SpriteRenderer Bar(string name, Vector3 pos, Vector3 scale, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(container.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Px();
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    private static Sprite Px()
    {
        if (_px != null) return _px;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        _px = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
        return _px;
    }
}
