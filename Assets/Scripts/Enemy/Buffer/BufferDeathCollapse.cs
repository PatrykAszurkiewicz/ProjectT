using System.Collections.Generic;
using UnityEngine;

//  BufferDeathCollapse 

[DisallowMultipleComponent]
public class BufferDeathCollapse : MonoBehaviour
{
    private float duration = 0.85f;
    private float elapsed;
    private bool playing;

    private Transform pivot;
    private Transform shadow;
    private float groundY;

    private Vector3 pivotStartPos;
    private Vector3 pivotStartScale = Vector3.one;
    private Vector3 shadowStartScale;
    private float scale = 1f;

    private readonly List<Transform> debris = new List<Transform>();
    private readonly List<Vector3> debrisVel = new List<Vector3>();
    private readonly List<float> debrisSpin = new List<float>();
    private readonly List<Vector3> debrisScale0 = new List<Vector3>();

    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Color> baseColors = new List<Color>();

    private Material ownedMaterial;
    private readonly List<Mesh> ownedMeshes = new List<Mesh>();

    private Transform burst;
    private MeshRenderer burstRenderer;
    private Color burstColor = Color.white;
    private Mesh burstMesh;

    private MaterialPropertyBlock mpb;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public void Play(float duration,
                     Transform pivot,
                     Transform shadow,
                     List<Transform> debris,
                     Material ownedMaterial,
                     List<Mesh> ownedMeshes,
                     Color burstColor,
                     float groundY,
                     float bodyCenterY,
                     float scale)
    {
        this.duration = Mathf.Max(0.1f, duration);
        this.pivot = pivot;
        this.shadow = shadow;
        this.ownedMaterial = ownedMaterial;
        this.burstColor = burstColor;
        this.groundY = groundY;
        // Debris throw distance, gravity and burst radius are all world-space,
        // so a 3x Buffer must scatter 3x as far or the collapse looks like a
        // small enemy dying inside a large silhouette.
        this.scale = Mathf.Max(0.01f, scale);

        if (ownedMeshes != null) this.ownedMeshes.AddRange(ownedMeshes);

        mpb = new MaterialPropertyBlock();

        if (pivot != null)
        {
            pivotStartPos = pivot.localPosition;
            // Captured, not assumed: at the moment of death he may have been
            // mid-turn, so scale.x is somewhere between -1 and 1.
            pivotStartScale = pivot.localScale;
        }
        if (shadow != null) shadowStartScale = shadow.localScale;

        // Debris is lifted out of the body before the body starts squashing,
        // otherwise the shards would inherit the collapse and implode with it
        // instead of scattering.
        if (debris != null)
        {
            for (int i = 0; i < debris.Count; i++)
            {
                var d = debris[i];
                if (d == null) continue;

                d.SetParent(transform, true);
                this.debris.Add(d);

                debrisScale0.Add(d.localScale);

                Vector3 outward = d.localPosition - new Vector3(0f, bodyCenterY, 0f);
                if (outward.sqrMagnitude < 0.0001f) outward = Vector3.up;
                outward = outward * (1f / Mathf.Max(0.0001f, outward.magnitude));

                debrisVel.Add(new Vector3(
                    outward.x * Random.Range(1.1f, 2.4f) + Random.Range(-0.4f, 0.4f),
                    Random.Range(1.4f, 3.0f),
                    0f) * this.scale);
                debrisSpin.Add(Random.Range(-620f, 620f));
            }
        }

        // Snapshot every renderer's current colour so the global fade is a
        // multiply rather than a reset — the freeze tint or hit flash he died
        // under stays correct all the way through the collapse.
        var found = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < found.Length; i++)
        {
            var r = found[i];
            if (r == null) continue;

            if (r is LineRenderer lr)
            {
                // Tethers and the censer chain: no property block, and the
                // tethers are already switched off by BufferVisual.
                lr.enabled = false;
                continue;
            }

            r.GetPropertyBlock(mpb);
            Color c = mpb.GetColor(ColorId);
            // A renderer that never got a block reads back as transparent
            // black; treat that as "untinted" rather than "invisible".
            if (c.r == 0f && c.g == 0f && c.b == 0f && c.a == 0f) c = Color.white;

            renderers.Add(r);
            baseColors.Add(c);
        }

        BuildBurst();
        playing = true;
    }

    private void BuildBurst()
    {
        burstMesh = BuildRingMesh(30, 0.30f, 0.5f);

        var go = new GameObject("DeathBurst");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, groundY, 0f);
        burst = go.transform;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = burstMesh;

        burstRenderer = go.AddComponent<MeshRenderer>();
        burstRenderer.sharedMaterial = ownedMaterial;

        // Sit just above whatever the body was drawing at, so the burst is
        // never swallowed by the collapsing robe.
        if (renderers.Count > 0)
        {
            burstRenderer.sortingLayerName = renderers[0].sortingLayerName;
            burstRenderer.sortingOrder = renderers[0].sortingOrder + 6;
        }

        burst.localScale = Vector3.zero;
    }

    private void Update()
    {
        if (!playing) return;

        float dt = Time.deltaTime;
        elapsed += dt;
        float t = Mathf.Clamp01(elapsed / duration);

        // A hard bright flare on the first ~15% sells the moment of death, then
        // everything drains. Without it the collapse reads as a fade-out rather
        // than a kill.
        float flare = t < 0.15f ? 1f + (1f - t / 0.15f) * 2.2f : 1f;
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.1f) / 0.9f));

        UpdateBody(t);
        UpdateDebris(dt, t);
        UpdateShadow(t);
        UpdateBurst(t);
        ApplyFade(flare, fade);

        if (t >= 1f) Finish();
    }

    private void UpdateBody(float t)
    {
        if (pivot == null) return;

        // Loses its shape from the top down: the robe crumples outward at the
        // hem while the hood drops through it.
        float squash = 1f - Mathf.SmoothStep(0f, 1f, t);
        pivot.localScale = new Vector3(
            pivotStartScale.x * (1f + t * 0.45f),
            pivotStartScale.y * Mathf.Max(0.02f, squash),
            1f);

        Vector3 p = pivotStartPos;
        p.y = Mathf.Lerp(pivotStartPos.y, groundY, Mathf.SmoothStep(0f, 1f, t));
        pivot.localPosition = p;
    }

    private void UpdateDebris(float dt, float t)
    {
        for (int i = 0; i < debris.Count; i++)
        {
            var d = debris[i];
            if (d == null) continue;

            Vector3 v = debrisVel[i];
            v.y -= 7.5f * scale * dt;   // light gravity — these are charms, not rocks
            v = v * 0.985f;
            debrisVel[i] = v;

            d.localPosition = d.localPosition + v * dt;
            d.localRotation = Quaternion.Euler(0f, 0f, debrisSpin[i] * elapsed);

            // Scaled from the CAPTURED start scale. Reading d.localScale back
            // and re-shrinking it would compound every frame and snap the
            // shards to nothing almost immediately.
            float shrink = Mathf.Max(0.01f, 1f - t * 0.85f);
            Vector3 s0 = debrisScale0[i];
            d.localScale = new Vector3(s0.x * shrink, s0.y * shrink, 1f);
        }
    }

    private void UpdateShadow(float t)
    {
        if (shadow == null) return;
        // Spreads as the body flattens onto it, then goes with everything else.
        float w = 1f + t * 0.5f;
        shadow.localScale = new Vector3(shadowStartScale.x * w, shadowStartScale.y * w, 1f);
    }

    private void UpdateBurst(float t)
    {
        if (burst == null) return;

        float bt = Mathf.Clamp01(t / 0.55f);
        float e = 1f - (1f - bt) * (1f - bt);
        float r = 1.9f * scale * e;

        burst.localScale = new Vector3(r, r * 0.45f, 1f);

        if (burstRenderer != null)
        {
            Color c = burstColor;
            c.a = (1f - bt) * (1f - bt) * 0.95f;
            mpb.SetColor(ColorId, c);
            burstRenderer.SetPropertyBlock(mpb);
        }
    }

    private void ApplyFade(float flare, float fade)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            if (r == burstRenderer) continue; // burst runs its own curve

            Color c = baseColors[i];
            c = new Color(c.r * flare, c.g * flare, c.b * flare, Mathf.Clamp01(c.a * fade));
            mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }

    private void Finish()
    {
        playing = false;

        // This component owns the rig now, so it is responsible for the
        // generated assets BufferVisual deliberately did not destroy.
        for (int i = 0; i < ownedMeshes.Count; i++)
            if (ownedMeshes[i] != null) Destroy(ownedMeshes[i]);
        ownedMeshes.Clear();

        if (burstMesh != null) Destroy(burstMesh);
        if (ownedMaterial != null) Destroy(ownedMaterial);

        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // Scene unload / wave reset mid-collapse: still don't leak.
        if (!playing) return;

        for (int i = 0; i < ownedMeshes.Count; i++)
            if (ownedMeshes[i] != null) Destroy(ownedMeshes[i]);
        if (burstMesh != null) Destroy(burstMesh);
        if (ownedMaterial != null) Destroy(ownedMaterial);
    }

    // Local copy of the annulus builder. Deliberately not shared with
    // BufferVisual: this component has to keep working after BufferVisual has
    // already been destroyed along with the enemy.
    private static Mesh BuildRingMesh(int segments, float innerR, float outerR)
    {
        segments = Mathf.Max(8, segments);
        float midR = (innerR + outerR) * 0.5f;
        float[] radii = { innerR, midR, outerR };
        float[] alphas = { 0f, 1f, 0f };

        var verts = new Vector3[segments * 3];
        var cols = new Color[verts.Length];

        for (int ring = 0; ring < 3; ring++)
        {
            for (int s = 0; s < segments; s++)
            {
                float a = (s / (float)segments) * Mathf.PI * 2f;
                int idx = ring * segments + s;
                verts[idx] = new Vector3(Mathf.Cos(a) * radii[ring], Mathf.Sin(a) * radii[ring], 0f);
                cols[idx] = new Color(1f, 1f, 1f, alphas[ring]);
            }
        }

        var tris = new List<int>(segments * 12);
        for (int ring = 0; ring < 2; ring++)
        {
            int inner = ring * segments;
            int outer = (ring + 1) * segments;
            for (int s = 0; s < segments; s++)
            {
                int s2 = (s + 1) % segments;
                tris.Add(inner + s); tris.Add(outer + s); tris.Add(inner + s2);
                tris.Add(inner + s2); tris.Add(outer + s); tris.Add(outer + s2);
            }
        }

        var m = new Mesh { name = "BufferDeathBurst" };
        m.vertices = verts;
        m.colors = cols;
        m.triangles = tris.ToArray();
        m.RecalculateBounds();
        return m;
    }
}



