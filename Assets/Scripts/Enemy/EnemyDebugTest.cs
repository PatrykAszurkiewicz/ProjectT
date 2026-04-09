using UnityEngine;


// Can be attached to any enemy prefab to display attack/idle/parry frames information
public class EnemyDebugTest : MonoBehaviour
{
    private GameObject debugMarker;
    private TextMesh debugText;

    void Start()
    {
        Debug.Log($"[EnemyDebugTest] ALIVE on '{gameObject.name}' at {transform.position} scale={transform.lossyScale}");

        // Inverse scale so debug is always the same world size
        float s = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, 0.01f);
        float inv = 1f / s;

        // Container
        GameObject container = new GameObject("__DEBUG_TEST__");
        container.transform.SetParent(transform, false);
        container.transform.localScale = Vector3.one * inv;

        // Big bright magenta square — impossible to miss
        debugMarker = new GameObject("DebugSquare");
        debugMarker.transform.SetParent(container.transform, false);
        debugMarker.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        debugMarker.transform.localScale = new Vector3(1f, 0.15f, 1f);

        SpriteRenderer sr = debugMarker.AddComponent<SpriteRenderer>();
        // Create a 1x1 white pixel sprite
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
        sr.color = Color.magenta;
        sr.sortingOrder = 10000;

        // Text
        GameObject textGO = new GameObject("DebugText");
        textGO.transform.SetParent(container.transform, false);
        textGO.transform.localPosition = new Vector3(0f, 1.7f, 0f);

        debugText = textGO.AddComponent<TextMesh>();
        debugText.text = "DEBUG WORKS";
        debugText.characterSize = 0.05f;
        debugText.fontSize = 40;
        debugText.fontStyle = FontStyle.Bold;
        debugText.anchor = TextAnchor.LowerCenter;
        debugText.alignment = TextAlignment.Center;
        debugText.color = Color.white;

        MeshRenderer mr = textGO.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sortingOrder = 10001;
        }
    }

    void Update()
    {
        if (debugText != null)
        {
            // Show current sprite name
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            string spriteName = (sr != null && sr.sprite != null) ? sr.sprite.name : "no sprite";
            debugText.text = $"{spriteName}";

            // Keep text facing camera (no rotation from parent)
            debugText.transform.parent.rotation = Quaternion.identity;
        }
    }
}
