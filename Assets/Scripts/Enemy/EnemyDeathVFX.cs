using UnityEngine;
using System.Collections;
using System.Collections.Generic;


// Enemy death VFX.
// Animation driven by Update() (not coroutines) on an independent host GameObject.


public class EnemyDeathVFX : MonoBehaviour
{
    private const float DISINTEGRATION_THRESHOLD = 1.0f;


    public static void Trigger(
        GameObject enemy,
        float duration = 1.5f,
        System.Action onComplete = null)
    {

        //Debug.Log($"[VFX-TRACE-1] Trigger() ENTERED. enemy='{enemy?.name ?? "NULL"}' duration={duration}");

        if (enemy == null)
        {
            Debug.LogError("[VFX] Trigger called with null enemy!");
            return;
        }
        // Don't spawn VFX during scene teardown
        if (!enemy.scene.isLoaded)
        {
            onComplete?.Invoke();
            return;
        }

        // Snapshot from live object before any mutation
        SpriteRenderer sr = enemy.GetComponentInChildren<SpriteRenderer>();
        //Debug.Log($"[VFX-TRACE-2] SpriteRenderer={sr != null}  " +
        //          $"sprite={(sr?.sprite != null ? sr.sprite.name : "NULL")}  " +
        //          $"renderer.enabled={sr?.enabled}");

        Sprite sprite = sr?.sprite;
        bool flipX = sr != null && sr.flipX;
        int sortOrder = sr?.sortingOrder ?? 10;
        string sortLayerName = sr?.sortingLayerName ?? "Default";
        int sortLayerID = sr?.sortingLayerID ?? 0;
        Vector3 worldPos = enemy.transform.position;
        Vector3 enemyScale = enemy.transform.lossyScale;

        Bounds worldBounds = (sr != null && sr.sprite != null)
            ? sr.bounds
            : new Bounds(worldPos, new Vector3(2f, 2f, 0f));

        //Debug.Log($"[VFX-TRACE-2b] worldPos={worldPos}  bounds.size={worldBounds.size}  " +
        //          $"scale={enemyScale}  layer='{sortLayerName}'({sortLayerID})  order={sortOrder}");


        int rendererCount = 0, rbCount = 0, colCount = 0, mbCount = 0;
        foreach (var ren in enemy.GetComponentsInChildren<Renderer>())
        { ren.enabled = false; rendererCount++; }
        foreach (var rb in enemy.GetComponentsInChildren<Rigidbody2D>())
        {
            rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Static; rb.simulated = false;
            rbCount++;
        }
        foreach (var col in enemy.GetComponentsInChildren<Collider2D>())
        { col.enabled = false; colCount++; }
        foreach (var mb in enemy.GetComponentsInChildren<MonoBehaviour>())
        { mb.enabled = false; mbCount++; }

        //Debug.Log($"[VFX-TRACE-3] Disabled: {rendererCount} renderers, {rbCount} rigidbodies, " +
        //          $"{colCount} colliders, {mbCount} monobehaviours");


        if (sprite == null)
        {
            Debug.LogWarning("[VFX-TRACE-4] sprite is NULL — destroying immediately with no VFX.");
            Object.Destroy(enemy);
            onComplete?.Invoke();
            return;
        }
        //Debug.Log($"[VFX-TRACE-4] Sprite OK: '{sprite.name}'  PPU={sprite.pixelsPerUnit}  " +
        //          $"texRect={sprite.textureRect}");

        // ══ TRACE 5 — build VFX data synchronously ═══════════════════
        bool isBoss = duration >= DISINTEGRATION_THRESHOLD;
        //Debug.Log($"[VFX-TRACE-5] isBoss={isBoss}  (threshold={DISINTEGRATION_THRESHOLD})");

        var chunks = new List<ChunkData>(80);
        var embers = new List<PtclData>(60);

        if (isBoss)
        {
            //Debug.Log("[VFX-TRACE-5a] Attempting TryBuildSpriteChunks...");
            bool fromSprite = TryBuildSpriteChunks(
                chunks, sprite, flipX,
                sortOrder, sortLayerName, sortLayerID,
                enemyScale, worldPos);
            //Debug.Log($"[VFX-TRACE-5b] TryBuildSpriteChunks returned {fromSprite}, chunks.Count={chunks.Count}");

            if (!fromSprite || chunks.Count == 0)
            {
                //Debug.Log("[VFX-TRACE-5c] Falling back to BuildFallbackChunks.");
                BuildFallbackChunks(chunks, worldBounds,
                    sortOrder, sortLayerName, sortLayerID, worldPos);
                //Debug.Log($"[VFX-TRACE-5d] Fallback produced {chunks.Count} chunks.");
            }
        }
        else
        {
            BuildClassicChunks(chunks, worldPos, worldBounds,
                sortOrder, sortLayerName, sortLayerID);
            //Debug.Log($"[VFX-TRACE-5e] Classic chunks: {chunks.Count}");
        }

        BuildEmbers(embers, worldPos, worldBounds,
            sortOrder, sortLayerName, sortLayerID);
        //Debug.Log($"[VFX-TRACE-5f] Embers built: {embers.Count}");


        GameObject host = new GameObject("[EnemyDeathVFX_Host]");
        host.transform.position = worldPos;

        EnemyDeathVFX vfx = host.AddComponent<EnemyDeathVFX>();
        vfx._chunks = chunks;
        vfx._embers = embers;
        vfx._duration = duration;
        vfx._isBoss = isBoss;
        vfx._enemy = enemy;
        vfx._onComplete = onComplete;

        //Debug.Log($"[VFX-TRACE-6] Host '{host.name}' created at {worldPos}.  " +
        //          $"AddComponent<EnemyDeathVFX> done.  Reparenting chunks to host...");

        // Parent all chunks/embers to host so they live/die with it
        vfx.ReparentChildren();

        vfx.StartCoroutine(vfx.DoFlash(
            worldPos, sprite, flipX,
            sortOrder, sortLayerName, sortLayerID, enemyScale));

        if (isBoss)
            vfx.StartCoroutine(vfx.DoShockwave(
                worldPos, sortOrder, sortLayerName,
                worldBounds.extents.magnitude * 2.5f));

        // Night mode: brief burst of light at the death position
        if (NightOverlay.Instance != null)
            vfx.StartCoroutine(vfx.DoNightDeathFlash(worldPos, isBoss));

        //Debug.Log("[VFX-TRACE-6b] Trigger() complete — host Update() will animate from next frame.");
    }



    private void Awake()
    {
        // This fires as soon as AddComponent<EnemyDeathVFX>() is called.
        //Debug.Log($"[VFX-HOST-AWAKE] EnemyDeathVFX Awake() on '{gameObject.name}' " +
        //          $"instanceID={gameObject.GetInstanceID()}");
    }

    private void OnDestroy()
    {
        //Debug.Log($"[VFX-HOST-DESTROY] EnemyDeathVFX host '{gameObject.name}' destroyed. " +
        //          $"elapsed={_elapsed:F3}/{_duration:F3}  done={_done}");
    }



    private List<ChunkData> _chunks;
    private List<PtclData> _embers;
    private float _duration;
    private bool _isBoss;
    private GameObject _enemy;
    private System.Action _onComplete;

    private float _elapsed = 0f;
    private bool _firstUpdate = true;
    private bool _done = false;

    private void Update()
    {
        if (_done) return;

        if (_firstUpdate)
        {
            _firstUpdate = false;
            //Debug.Log($"[VFX-UPDATE-1] FIRST Update() tick on host '{gameObject.name}'.  " +
            //          $"chunks={_chunks?.Count ?? -1}  embers={_embers?.Count ?? -1}  " +
            //          $"duration={_duration}  isBoss={_isBoss}");
        }

        _elapsed += Time.deltaTime;

        TickChunks(_elapsed);
        TickEmbers(_elapsed);

        if (_elapsed >= _duration)
        {
            _done = true;
            //Debug.Log($"[VFX-UPDATE-DONE] Animation finished at elapsed={_elapsed:F3}. Destroying enemy and host.");
            if (_enemy != null) Destroy(_enemy);
            _onComplete?.Invoke();
            Destroy(gameObject);  // also destroys all parented chunks/embers
        }
    }

    // ─────────────────────────────────────────────────────────────────

    private void TickChunks(float elapsed)
    {
        if (_chunks == null) return;
        foreach (var c in _chunks)
        {
            if (c.go == null) continue;
            float age = elapsed - c.delay;
            if (age < 0f) continue;

            float t = Mathf.Clamp01(age / c.life);

            c.vel.y += c.grav * Time.deltaTime;
            c.go.transform.position += (Vector3)c.vel * Time.deltaTime;
            c.vel *= 0.975f;
            c.go.transform.Rotate(0f, 0f, c.rotSpeed * Time.deltaTime);

            float s = Mathf.Lerp(1f, 0f, t * t * t);
            c.go.transform.localScale = c.startScale * Mathf.Max(s, 0.001f);

            Color col = c.sr.color;
            if (_isBoss)
            {
                col.r = Mathf.Lerp(col.r, 1.00f, t * 0.35f);
                col.g = Mathf.Lerp(col.g, 0.25f, t * 0.45f);
                col.b = Mathf.Lerp(col.b, 0.00f, t * 0.55f);
            }
            col.a = 1f - t * t;
            c.sr.color = col;
        }
    }

    private void TickEmbers(float elapsed)
    {
        if (_embers == null) return;
        foreach (var p in _embers)
        {
            if (p.go == null) continue;
            float age = elapsed - p.delay;
            if (age < 0f) { p.sr.enabled = false; continue; }
            p.sr.enabled = true;
            float t = Mathf.Clamp01(age / p.life);
            p.vel.y += p.grav * Time.deltaTime;
            p.go.transform.position += (Vector3)p.vel * Time.deltaTime;
            p.vel *= 0.965f;
            p.sr.color = Color.Lerp(p.c0, p.c1, t * t);
            float ps = Mathf.Lerp(p.s0, 0f, t);
            p.go.transform.localScale = Vector3.one * Mathf.Max(0.001f, ps);
        }
    }


    private static bool TryBuildSpriteChunks(
        List<ChunkData> chunks,
        Sprite srcSprite, bool flipX,
        int sortOrder, string sortLayerName, int sortLayerID,
        Vector3 enemyScale, Vector3 origin)
    {
        Texture2D srcTex = srcSprite.texture;
        Rect texRect = srcSprite.textureRect;
        Vector2 pivot = srcSprite.pivot;
        float ppu = srcSprite.pixelsPerUnit;

        int gridX = Mathf.Clamp(Mathf.RoundToInt(texRect.width / 14f), 3, 14);
        int gridY = Mathf.Clamp(Mathf.RoundToInt(texRect.height / 14f), 3, 14);
        float cellW = texRect.width / gridX;
        float cellH = texRect.height / gridY;

        //Debug.Log($"[VFX-CHUNKS] grid={gridX}x{gridY}  cellSize={cellW:F1}x{cellH:F1}  " +
        //          $"texRect={texRect}  ppu={ppu}");

        Texture2D readable = MakeReadable(srcTex, texRect);
        if (readable == null)
        {
            Debug.LogWarning("[VFX-CHUNKS] MakeReadable returned null!");
            return false;
        }
        //Debug.Log($"[VFX-CHUNKS] readable texture: {readable.width}x{readable.height}");

        float localPivotX = pivot.x - texRect.x;
        float localPivotY = pivot.y - texRect.y;



        for (int cy = 0; cy < gridY; cy++)
            for (int cx = 0; cx < gridX; cx++)
            {
                int px = Mathf.FloorToInt(cx * cellW);
                int py = Mathf.FloorToInt(cy * cellH);
                int pw = Mathf.Min(Mathf.CeilToInt(cellW), readable.width - px);
                int ph = Mathf.Min(Mathf.CeilToInt(cellH), readable.height - py);
                if (pw <= 0 || ph <= 0) continue;

                Color[] cellPx = readable.GetPixels(px, py, pw, ph);
                bool hasContent = false;
                foreach (var c in cellPx) if (c.a > 0.05f) { hasContent = true; break; }
                if (!hasContent) continue;

                Texture2D ct = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
                ct.filterMode = FilterMode.Point;
                ct.SetPixels(cellPx); ct.Apply();
                Sprite cs = Sprite.Create(ct, new Rect(0, 0, pw, ph), new Vector2(0.5f, 0.5f), ppu);

                float pixCX = px + pw * 0.5f - localPivotX;
                float pixCY = py + ph * 0.5f - localPivotY;
                float woX = (flipX ? -pixCX : pixCX) / ppu * enemyScale.x;
                float woY = pixCY / ppu * enemyScale.y;
                Vector3 cWorldPos = origin + new Vector3(woX, woY, 0f);

                GameObject go = new GameObject("DC");
                go.transform.position = cWorldPos;
                go.transform.localScale = enemyScale;

                SpriteRenderer csr = go.AddComponent<SpriteRenderer>();
                csr.sprite = cs;
                csr.sortingLayerName = sortLayerName;
                csr.sortingLayerID = sortLayerID;
                csr.sortingOrder = sortOrder + 10 + Random.Range(0, 5);
                csr.flipX = flipX;
                csr.color = Color.white;

                Vector2 dir = (Vector2)(cWorldPos - origin);
                if (dir.sqrMagnitude < 0.01f) dir = Random.insideUnitCircle;
                dir = (dir.normalized * 0.6f + (Vector2)Random.insideUnitCircle * 0.4f).normalized;

                chunks.Add(new ChunkData
                {
                    go = go,
                    sr = csr,
                    worldPos = cWorldPos,
                    vel = dir * Random.Range(1.5f, 4.5f),
                    rotSpeed = Random.Range(-360f, 360f),
                    delay = Random.Range(0f, 0.12f),
                    life = Random.Range(0.7f, 1.4f),
                    grav = Random.Range(-3f, -0.5f),
                    startScale = enemyScale,
                });
            }

        Destroy(readable);
        return chunks.Count > 0;
    }

    private static void BuildFallbackChunks(
        List<ChunkData> chunks, Bounds bounds,
        int sortOrder, string sortLayerName, int sortLayerID,
        Vector3 origin)
    {
        Sprite spr = GetChunkSprite();
        for (int i = 0; i < 40; i++)
        {
            Vector3 pos = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y), 0f);

            GameObject go = new GameObject("FC");
            go.transform.position = pos;
            float sz = Random.Range(0.25f, 0.80f);
            go.transform.localScale = Vector3.one * sz;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spr;
            sr.sortingLayerName = sortLayerName;
            sr.sortingLayerID = sortLayerID;
            sr.sortingOrder = sortOrder + 10;
            sr.color = new Color(Random.Range(0.75f, 1f), Random.Range(0.1f, 0.55f), 0f, 1f);

            Vector2 dir = (Vector2)(pos - origin);
            if (dir.sqrMagnitude < 0.01f) dir = Random.insideUnitCircle;
            dir = (dir.normalized + (Vector2)Random.insideUnitCircle * 0.5f).normalized;

            chunks.Add(new ChunkData
            {
                go = go,
                sr = sr,
                worldPos = pos,
                vel = dir * Random.Range(2f, 5.5f),
                rotSpeed = Random.Range(-300f, 300f),
                delay = Random.Range(0f, 0.15f),
                life = Random.Range(0.7f, 1.5f),
                grav = Random.Range(-4f, -1f),
                startScale = Vector3.one * sz,
            });
        }
    }

    private static void BuildClassicChunks(
        List<ChunkData> chunks, Vector3 origin, Bounds bounds,
        int sortOrder, string sortLayerName, int sortLayerID)
    {
        Sprite spr = GetChunkSprite();
        for (int i = 0; i < 20; i++)
        {
            float wx = Random.Range(bounds.min.x, bounds.max.x);
            float wy = Random.Range(bounds.min.y, bounds.max.y);
            Vector3 pos = new Vector3(wx, wy, 0f);
            GameObject go = new GameObject("C");
            go.transform.position = pos;
            float sz = Random.Range(0.30f, 0.72f);
            go.transform.localScale = Vector3.one * sz;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spr;
            sr.sortingLayerName = sortLayerName;
            sr.sortingLayerID = sortLayerID;
            sr.sortingOrder = sortOrder + 10;
            sr.color = new Color(Random.Range(0.75f, 1f), Random.Range(0.1f, 0.45f), 0f, 1f);

            Vector2 d = new Vector2(wx - origin.x, wy - origin.y);
            if (d.sqrMagnitude < 0.001f) d = Random.insideUnitCircle;
            Vector2 vel = (d.normalized * 0.6f + Vector2.up * 0.4f +
                           (Vector2)Random.insideUnitCircle * 0.9f).normalized * Random.Range(2f, 5f);

            chunks.Add(new ChunkData
            {
                go = go,
                sr = sr,
                worldPos = pos,
                vel = vel,
                startScale = Vector3.one * sz,
                rotSpeed = Random.Range(-280f, 280f),
                delay = (1f - Mathf.InverseLerp(bounds.min.y, bounds.max.y, wy)) * 0.20f,
                life = Random.Range(0.50f, 0.95f),
                grav = Random.Range(-4.5f, -1.5f),
            });
        }
    }

    private static void BuildEmbers(
        List<PtclData> list, Vector3 origin, Bounds bounds,
        int sortOrder, string sortLayerName, int sortLayerID)
    {
        Sprite spr = GetEmberSprite();
        for (int i = 0; i < 55; i++)
        {
            float wx = Random.Range(bounds.min.x, bounds.max.x);
            float wy = Random.Range(bounds.min.y, bounds.max.y);
            Vector3 pos = new Vector3(wx, wy, 0f);
            GameObject go = new GameObject("E");
            go.transform.position = pos;
            float sz = Random.Range(0.08f, 0.28f);
            go.transform.localScale = Vector3.one * sz;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spr;
            sr.sortingLayerName = sortLayerName;
            sr.sortingLayerID = sortLayerID;
            sr.sortingOrder = sortOrder + 15;
            sr.enabled = false;

            Color c0 = Color.Lerp(new Color(1f, 0.95f, 0.25f, 1f), new Color(1f, 0.2f, 0f, 1f), Random.value);
            sr.color = c0;

            Vector2 d = new Vector2(wx - origin.x, wy - origin.y);
            if (d.sqrMagnitude < 0.001f) d = Random.insideUnitCircle;
            Vector2 vel = (d.normalized * 0.35f + Vector2.up * 0.75f +
                           (Vector2)Random.insideUnitCircle * 1.2f).normalized * Random.Range(2.5f, 7f);

            list.Add(new PtclData
            {
                go = go,
                sr = sr,
                worldPos = pos,
                vel = vel,
                s0 = sz,
                grav = Random.Range(-2.5f, -0.3f),
                c0 = c0,
                c1 = new Color(0.07f, 0f, 0f, 0f),
                delay = (1f - Mathf.InverseLerp(bounds.min.y, bounds.max.y, wy)) * 0.16f,
                life = Random.Range(0.35f, 0.80f),
            });
        }
    }


    internal void ReparentChildren()
    {
        if (_chunks != null)
            foreach (var c in _chunks)
                if (c.go != null)
                    c.go.transform.SetParent(transform, true); // worldPositionStays=true

        if (_embers != null)
            foreach (var p in _embers)
                if (p.go != null)
                    p.go.transform.SetParent(transform, true);

        //Debug.Log($"[VFX-REPARENT] Parented {_chunks?.Count ?? 0} chunks + " +
        //          $"{_embers?.Count ?? 0} embers to host '{gameObject.name}'.");
    }




    private IEnumerator DoFlash(
        Vector3 origin, Sprite sprite, bool flipX,
        int sortOrder, string sortLayerName, int sortLayerID, Vector3 scale)
    {
        //Debug.Log("[VFX-FLASH] DoFlash coroutine started.");
        GameObject fObj = new GameObject("Flash");
        fObj.transform.SetParent(transform, false);  // parent to host
        fObj.transform.position = origin;
        fObj.transform.localScale = scale;

        SpriteRenderer fsr = fObj.AddComponent<SpriteRenderer>();
        fsr.sprite = sprite;
        fsr.sortingLayerName = sortLayerName;
        fsr.sortingLayerID = sortLayerID;
        fsr.sortingOrder = sortOrder + 50;
        fsr.flipX = flipX;
        fsr.color = Color.white;
        //Debug.Log($"[VFX-FLASH] Flash SR: layer='{sortLayerName}' order={sortOrder + 50} sprite='{sprite.name}'");

        float t = 0f;
        while (t < 0.12f)
        {
            t += Time.deltaTime;
            if (fsr != null)
                fsr.color = new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0f, t / 0.12f));
            yield return null;
        }
        if (fObj != null) Destroy(fObj);
        //Debug.Log("[VFX-FLASH] DoFlash coroutine complete.");
    }

    private IEnumerator DoShockwave(
        Vector3 origin, int sortOrder, string sortLayerName, float maxRadius)
    {
        //Debug.Log("[VFX-SHOCKWAVE] DoShockwave started.");
        GameObject go = new GameObject("Ring");
        go.transform.SetParent(transform, false);
        go.transform.position = origin;

        LineRenderer lr = go.AddComponent<LineRenderer>();
        Material mat = new Material(Shader.Find("Sprites/Default"));
        lr.material = mat;
        lr.startWidth = 0.22f;
        lr.endWidth = 0.22f;
        lr.sortingOrder = sortOrder + 25;
        lr.sortingLayerName = sortLayerName;
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = 49;

        float dur = 0.42f, e = 0f;
        while (e < dur)
        {
            e += Time.deltaTime;
            float t = e / dur;
            float r = Mathf.Lerp(0.05f, maxRadius, Mathf.Sqrt(t));
            mat.color = new Color(1f, 0.55f, 0.08f, 1f - t * t);
            for (int i = 0; i < 49; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                lr.SetPosition(i, origin + new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            yield return null;
        }
        if (go != null) Destroy(go);
        //Debug.Log("[VFX-SHOCKWAVE] DoShockwave complete.");
    }

    /// Brief burst of illumination through the night overlay when an enemy disintegrates.
    private IEnumerator DoNightDeathFlash(Vector3 origin, bool isBoss)
    {
        if (NightOverlay.Instance == null) yield break;

        float flashRadius = isBoss ? 5f : 2.5f;
        float peakIntensity = isBoss ? 0.7f : 0.4f;
        float duration = isBoss ? 0.5f : 0.3f;
        Color flashColor = isBoss
            ? new Color(1f, 0.55f, 0.08f)    // warm orange for boss
            : new Color(1f, 0.7f, 0.3f);      // softer warm for regular

        var handle = NightOverlay.RegisterLight(
            origin, flashRadius, 0f, flashColor, 0.5f);

        if (handle == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Sharp rise, smooth decay
            float envelope = t < 0.15f
                ? Mathf.Clamp01(t / 0.15f)
                : Mathf.Pow(1f - (t - 0.15f) / 0.85f, 2f);

            handle.intensity = peakIntensity * envelope;
            handle.radius = flashRadius * (1f + t * 0.3f);  // slight expansion
            yield return null;
        }

        NightOverlay.UnregisterLight(handle);
    }


    // TEXTURE UTILITY

    private static Texture2D MakeReadable(Texture2D src, Rect rect)
    {
        int x = Mathf.FloorToInt(rect.x);
        int y = Mathf.FloorToInt(rect.y);
        int w = Mathf.Min(Mathf.CeilToInt(rect.width), src.width - x);
        int h = Mathf.Min(Mathf.CeilToInt(rect.height), src.height - y);
        if (w <= 0 || h <= 0)
        {
            Debug.LogWarning($"[VFX] MakeReadable: invalid region w={w} h={h}");
            return null;
        }

        // Direct CPU read
        try
        {
            Color[] px = src.GetPixels(x, y, w, h);
            Texture2D r = new Texture2D(w, h, TextureFormat.RGBA32, false);
            r.filterMode = FilterMode.Point;
            r.SetPixels(px); r.Apply();
            //Debug.Log("[VFX] Texture read via GetPixels (isReadable=true).");
            return r;
        }
        catch (System.Exception ex1)
        {
            Debug.Log($"[VFX] GetPixels failed ({ex1.Message}), trying GPU blit");
        }

        // GPU blit fallback (works even when Read/Write is off)
        try
        {
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            Texture2D r = new Texture2D(w, h, TextureFormat.RGBA32, false);
            r.filterMode = FilterMode.Point;
            r.ReadPixels(new Rect(x, y, w, h), 0, 0); r.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            //Debug.Log("[VFX] Texture read via GPU blit (isReadable=false).");
            return r;
        }
        catch (System.Exception ex2)
        {
            Debug.LogWarning($"[VFX] Both texture reads failed: {ex2.Message}");
            return null;
        }
    }


    // PROCEDURAL SPRITES


    private static Sprite _chunkSprite, _emberSprite;

    private static Sprite GetChunkSprite()
    {
        if (_chunkSprite != null) return _chunkSprite;
        var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var px = new Color[256];
        Vector2[] poly = { new(8, 15), new(14, 10), new(12, 2), new(3, 2), new(1, 9) };
        for (int iy = 0; iy < 16; iy++)
            for (int ix = 0; ix < 16; ix++)
                px[iy * 16 + ix] = InPoly(ix, iy, poly) ? Color.white : Color.clear;
        tex.SetPixels(px); tex.Apply();
        _chunkSprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), Vector2.one * 0.5f, 32f);
        return _chunkSprite;
    }

    private static Sprite GetEmberSprite()
    {
        if (_emberSprite != null) return _emberSprite;
        var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color[256];
        var ctr = new Vector2(8f, 8f);
        for (int iy = 0; iy < 16; iy++)
            for (int ix = 0; ix < 16; ix++)
            {
                float v = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(ix, iy), ctr) / 8f);
                px[iy * 16 + ix] = new Color(1f, 1f, 1f, v * v);
            }
        tex.SetPixels(px); tex.Apply();
        _emberSprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), Vector2.one * 0.5f, 32f);
        return _emberSprite;
    }

    private static bool InPoly(int x, int y, Vector2[] poly)
    {
        int n = poly.Length, c = 0;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % n];
            if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y))
            {
                float t = (y - a.y) / (b.y - a.y);
                if (x < a.x + t * (b.x - a.x)) c++;
            }
        }
        return (c & 1) == 1;
    }


    private class ChunkData
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Vector3 worldPos;   // initial world position 
        public Vector2 vel;
        public float rotSpeed, delay, life, grav;
        public Vector3 startScale;
    }

    private class PtclData
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Vector3 worldPos;
        public Vector2 vel;
        public float grav, s0, delay, life;
        public Color c0, c1;
    }
}
