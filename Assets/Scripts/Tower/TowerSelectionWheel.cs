using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class TowerSelectionWheel : MonoBehaviour
{
    private GameObject[] towers;
    private TowerSlot targetSlot;
    private GameObject[] slices;
    private int hoveredIndex = -1;
    private bool isActive = false;

    void Update()
    {
        if (!isActive) return;
        if (Mouse.current == null || Keyboard.current == null) return;

        HandleInput();

        if (Keyboard.current.escapeKey.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)
        {
            CloseWheel();
        }
    }

    public void OpenWheel(GameObject[] towerArray, TowerSlot slot)
    {
        if (towerArray == null || towerArray.Length <= 1 || slot == null) return;
        towers = towerArray;
        targetSlot = slot;
        transform.position = slot.transform.position;
        if (slices != null)
        {
            foreach (GameObject slice in slices)
            {
                if (slice != null) DestroyImmediate(slice);
            }
        }
        transform.localScale = Vector3.one;

        CreatePieSlices();
        gameObject.SetActive(true);
        isActive = true;
    }

    public void CloseWheel()
    {
        isActive = false;
        gameObject.SetActive(false);
        CleanUp();
    }

    void CreatePieSlices()
    {
        CleanUp();

        int count = Mathf.Min(towers.Length, 6);
        slices = new GameObject[count];
        float angleStep = 360f / count;

        for (int i = 0; i < count; i++)
        {
            slices[i] = CreatePieSlice(i, angleStep);
        }
    }

    GameObject CreatePieSlice(int index, float angleStep)
    {
        float startAngle = index * angleStep;
        float midAngle = startAngle + angleStep * 0.5f;

        // Create slice object
        GameObject slice = new GameObject($"Slice{index}");
        slice.transform.parent = transform;
        slice.transform.localPosition = Vector3.zero;

        // Create pie slice sprite
        SpriteRenderer sr = slice.AddComponent<SpriteRenderer>();
        sr.sprite = CreatePieSliceSprite(startAngle, angleStep);
        sr.color = new Color(0.4f, 0.4f, 0.4f, 0.9f);
        sr.sortingOrder = 10;

        // Add tower name positioned outside the slice
        GameObject textObj = new GameObject("Text");
        textObj.transform.parent = slice.transform;

        float textRadius = 1.1f; // Much closer to the slice outer edge
        textObj.transform.localPosition = new Vector3(
            Mathf.Cos(midAngle * Mathf.Deg2Rad) * textRadius,
            Mathf.Sin(midAngle * Mathf.Deg2Rad) * textRadius,
            -0.1f
        );

        // Rotate text tangent to the circle
        float textRotation = midAngle + 90f; // 90 degrees offset

        // Adjust rotation for better readability by flipping text if it is upside down
        if (textRotation > 90f && textRotation < 270f)
        {
            textRotation += 180f; // Flip to keep text right-side up
        }

        textObj.transform.rotation = Quaternion.AngleAxis(textRotation, Vector3.forward);

        TextMesh text = textObj.AddComponent<TextMesh>();
        text.text = GetTowerName(index);
        text.fontSize = 15;
        text.color = Color.white;
        text.anchor = TextAnchor.MiddleCenter;
        text.characterSize = 0.2f;

        return slice;
    }

    string GetTowerName(int index)
    {
        if (index < towers.Length && towers[index] != null)
        {
            Tower tower = towers[index].GetComponent<Tower>();
            if (tower != null && !string.IsNullOrEmpty(tower.towerName))
            {
                string name = tower.towerName;
                // Truncate tower name to 11 characters
                if (name.Length > 11)
                    name = name.Substring(0, 11);
                return name;
            }
            return towers[index].name;
        }
        return $"Tower {index + 1}";
    }

    Sprite CreatePieSliceSprite(float startAngle, float angleSpan)
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];

        Vector2 center = Vector2.one * (size * 0.5f);
        float outerRadius = size * 0.45f;
        float innerRadius = size * 0.2f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                Vector2 pos = new Vector2(x, y) - center;
                float dist = pos.magnitude;
                float angle = Mathf.Atan2(pos.y, pos.x) * Mathf.Rad2Deg;
                if (angle < 0) angle += 360f;

                bool inRadius = dist >= innerRadius && dist <= outerRadius;
                bool inAngle = IsAngleInRange(angle, startAngle, startAngle + angleSpan);

                pixels[y * size + x] = (inRadius && inAngle) ? Color.white : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 64f);
    }

    bool IsAngleInRange(float angle, float start, float end)
    {
        if (end > 360f)
        {
            return angle >= start || angle <= (end - 360f);
        }
        return angle >= start && angle <= end;
    }

    void HandleInput()
    {
        if (slices == null) return;

        Vector3 mouse = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mouse.z = 0f;

        // Calculate direction from wheel center to mouse position
        Vector2 direction = (mouse - transform.position).normalized;

        // Convert direction to angle
        float mouseAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        if (mouseAngle < 0) mouseAngle += 360f;

        // Find which slice this angle corresponds to
        int newHover = -1;
        if (slices.Length > 0)
        {
            float angleStep = 360f / slices.Length;

            // Find the closest slice based on angle direction
            for (int i = 0; i < slices.Length; i++)
            {
                float sliceStartAngle = i * angleStep;
                float sliceEndAngle = sliceStartAngle + angleStep;

                // Adjust for wraparound at 0/360 degrees
                if (IsAngleInRange(mouseAngle, sliceStartAngle, sliceEndAngle))
                {
                    newHover = i;
                    break;
                }
            }
        }

        // Update hover colors
        if (newHover != hoveredIndex)
        {
            // Clear old hover
            if (hoveredIndex >= 0 && hoveredIndex < slices.Length && slices[hoveredIndex] != null)
            {
                slices[hoveredIndex].GetComponent<SpriteRenderer>().color = new Color(0.4f, 0.4f, 0.4f, 0.9f);
            }

            // Set new hover
            if (newHover >= 0 && newHover < slices.Length && slices[newHover] != null)
            {
                slices[newHover].GetComponent<SpriteRenderer>().color = new Color(0.6f, 0.8f, 1f, 1f);
            }

            hoveredIndex = newHover;
        }

        // Handle click
        if (Mouse.current.leftButton.wasPressedThisFrame && hoveredIndex >= 0)
        {
            SelectSlice(hoveredIndex);
        }
    }

    void SelectSlice(int index)
    {
        if (TowerPlacementManager.Instance != null && index < towers.Length)
        {
            TowerPlacementManager.Instance.PlaceTowerFromWheel(index, towers[index], targetSlot);
        }
        CloseWheel();
    }

    void CleanUp()
    {
        if (slices != null)
        {
            for (int i = 0; i < slices.Length; i++)
            {
                if (slices[i] != null)
                {
                    DestroyImmediate(slices[i]);
                }
            }
        }
        slices = null;
        hoveredIndex = -1;
    }

    IEnumerator ScaleUp()
    {
        transform.localScale = Vector3.one;
        yield break;
    }

    IEnumerator ScaleDown()
    {
        isActive = false;
        gameObject.SetActive(false);
        CleanUp();
        yield break;
    }

    void OnDisable()
    {
        isActive = false;
    }
}