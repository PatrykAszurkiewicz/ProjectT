using UnityEngine;

public class EnergyBar : MonoBehaviour
{
    [Header("Energy Bar Settings")]
    public bool showEnergyBar = true;
    public float energyBarHeight = 0.1f;
    public float energyBarWidth = 1f;
    public float energyBarOffset = 1.5f;
    public bool showEnergyText = true;

    [Header("Colors")]
    public Color backgroundBarColor = Color.black;
    public Color normalEnergyColor = Color.lightSteelBlue;
    public Color lowEnergyColor = Color.yellow;
    public Color criticalEnergyColor = Color.red;
    public Color depletedEnergyColor = Color.gray;

    private GameObject energyBarContainer;
    private SpriteRenderer energyBarBackground;
    private SpriteRenderer energyBarFill;
    private TextMesh energyText;
    private IEnergyConsumer energyConsumer;
    private SpriteRenderer parentSpriteRenderer;

    public void Initialize(IEnergyConsumer consumer, SpriteRenderer parentRenderer)
    {
        energyConsumer = consumer;
        parentSpriteRenderer = parentRenderer;
        CreateEnergyBar();
    }

    void Update()
    {
        if (energyConsumer != null && showEnergyBar)
        {
            UpdateEnergyBarVisuals();
        }
    }

    void CreateEnergyBar()
    {
        if (!showEnergyBar || energyConsumer == null) return;

        // Create energy bar container
        energyBarContainer = new GameObject("EnergyBar");
        energyBarContainer.transform.SetParent(transform);
        energyBarContainer.transform.localPosition = Vector3.up * energyBarOffset;

        // Create background bar
        GameObject backgroundObj = new GameObject("EnergyBarBackground");
        backgroundObj.transform.SetParent(energyBarContainer.transform);
        backgroundObj.transform.localPosition = Vector3.zero;
        energyBarBackground = backgroundObj.AddComponent<SpriteRenderer>();
        energyBarBackground.sprite = CreateColoredSprite(backgroundBarColor, (int)(energyBarWidth * 100), (int)(energyBarHeight * 100));

        if (parentSpriteRenderer != null)
        {
            energyBarBackground.sortingLayerName = parentSpriteRenderer.sortingLayerName;
            energyBarBackground.sortingOrder = parentSpriteRenderer.sortingOrder + 1;
        }

        // Create energy fill bar
        GameObject fillObj = new GameObject("EnergyBarFill");
        fillObj.transform.SetParent(energyBarContainer.transform);
        fillObj.transform.localPosition = Vector3.zero;
        energyBarFill = fillObj.AddComponent<SpriteRenderer>();
        energyBarFill.sprite = CreateColoredSprite(normalEnergyColor, (int)(energyBarWidth * 100), (int)(energyBarHeight * 100));

        if (parentSpriteRenderer != null)
        {
            energyBarFill.sortingLayerName = parentSpriteRenderer.sortingLayerName;
            energyBarFill.sortingOrder = parentSpriteRenderer.sortingOrder + 2;
        }

        // Create energy text
        if (showEnergyText)
        {
            GameObject textObj = new GameObject("EnergyText");
            textObj.transform.SetParent(energyBarContainer.transform);
            textObj.transform.localPosition = Vector3.up * 0.3f;
            energyText = textObj.AddComponent<TextMesh>();
            energyText.text = $"{energyConsumer.GetEnergy():F0}/{energyConsumer.GetMaxEnergy():F0}";
            energyText.fontSize = 17;
            energyText.characterSize = 0.14f;
            energyText.anchor = TextAnchor.MiddleCenter;
            energyText.color = normalEnergyColor;

            // Set text sorting order
            MeshRenderer textRenderer = textObj.GetComponent<MeshRenderer>();
            if (textRenderer != null && parentSpriteRenderer != null)
            {
                textRenderer.sortingLayerName = parentSpriteRenderer.sortingLayerName;
                textRenderer.sortingOrder = parentSpriteRenderer.sortingOrder + 3;
            }
        }
    }

    void UpdateEnergyBarVisuals()
    {
        if (energyBarFill == null || energyBarBackground == null || energyConsumer == null) return;
        if (EnergyManager.Instance == null) return;
        float energyPercentage = energyConsumer.GetEnergyPercentage();
        // Determine energy bar color based on energy state
        Color energyColor = normalEnergyColor;
        if (energyConsumer.IsEnergyDepleted())
            energyColor = depletedEnergyColor;
        else if (energyConsumer.IsEnergyLow())
            energyColor = Color.Lerp(criticalEnergyColor, lowEnergyColor, energyPercentage / EnergyManager.Instance.GetCriticalThreshold(energyConsumer));
        else
            energyColor = normalEnergyColor;
        energyBarFill.color = energyColor;

        // Update energy bar fill scale to represent energy percentage
        Vector3 fillScale = new Vector3(energyPercentage, 1f, 1f);
        energyBarFill.transform.localScale = fillScale;
        // Adjust fill position to align with background
        Vector3 fillPosition = Vector3.left * (energyBarWidth * (1f - energyPercentage) * 0.5f);
        energyBarFill.transform.localPosition = fillPosition;
        // Update energy text
        if (energyText != null && showEnergyText)
        {
            energyText.text = $"{energyConsumer.GetEnergy():F0}/{energyConsumer.GetMaxEnergy():F0}";
            energyText.color = energyColor;
        }
    }

    Sprite CreateColoredSprite(Color color, int width, int height)
    {
        Texture2D texture = new Texture2D(width, height);
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
    }

    public void SetVisibility(bool visible)
    {
        showEnergyBar = visible;
        if (energyBarContainer != null)
        {
            energyBarContainer.SetActive(visible);
        }
    }

    public void SetColors(Color normal, Color low, Color critical, Color depleted)
    {
        normalEnergyColor = normal;
        lowEnergyColor = low;
        criticalEnergyColor = critical;
        depletedEnergyColor = depleted;
    }

    void OnDestroy()
    {
        if (energyBarContainer != null)
        {
            DestroyImmediate(energyBarContainer);
        }
    }
}