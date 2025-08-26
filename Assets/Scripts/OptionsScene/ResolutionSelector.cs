using UnityEngine;
using TMPro;

public class ResolutionSelector : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI resolutionText;   // TMP text named "Resolution"
    [SerializeField] private TextMeshProUGUI displayModeText;  // TMP text named "DisplayMode"

    // Data
    private Resolution[] resolutions;
    private int resIndexPending = 0;

    private enum DisplayMode { Windowed, Borderless, Fullscreen }
    private readonly DisplayMode[] displayModes =
        { DisplayMode.Windowed, DisplayMode.Borderless, DisplayMode.Fullscreen };
    private int modeIndexPending = 0;

    private void Start()
    {
        resolutions = Screen.resolutions;

        // Start current res
        resIndexPending = FindIndexMatching(Screen.currentResolution);
        modeIndexPending = ModeToIndex(Screen.fullScreenMode);

        UpdateResolutionText();
        UpdateDisplayModeText();
    }

    // --- Res ---
    public void OnResLeft()
    {
        resIndexPending = (resIndexPending - 1 + resolutions.Length) % resolutions.Length;
        UpdateResolutionText();
    }

    public void OnResRight()
    {
        resIndexPending = (resIndexPending + 1) % resolutions.Length;
        UpdateResolutionText();
    }

    // --- Tryb ---
    public void OnModeLeft()
    {
        modeIndexPending = (modeIndexPending - 1 + displayModes.Length) % displayModes.Length;
        UpdateDisplayModeText();
    }

    public void OnModeRight()
    {
        modeIndexPending = (modeIndexPending + 1) % displayModes.Length;
        UpdateDisplayModeText();
    }

    // --- Apply ---
    public void OnApply()
    {
        var res = resolutions[resIndexPending];
        var rr = res.refreshRateRatio;

        var mode = IndexToMode(modeIndexPending);
        Screen.SetResolution(res.width, res.height, mode, rr);
    }

    // --- UI helpers ---
    private void UpdateResolutionText()
    {
        var r = resolutions[resIndexPending];
        int hz = (int)(r.refreshRateRatio.numerator / (float)r.refreshRateRatio.denominator);
        if (resolutionText) resolutionText.text = $"{r.width} x {r.height} @ {hz}Hz";
    }

    private void UpdateDisplayModeText()
    {
        if (displayModeText) displayModeText.text = displayModes[modeIndexPending].ToString();
    }

    private int ModeToIndex(FullScreenMode mode)
    {
        return mode switch
        {
            FullScreenMode.Windowed => 0,
            FullScreenMode.FullScreenWindow => 1,   // Borderless
            FullScreenMode.ExclusiveFullScreen => 2, // Fullscreen
            _ => 0
        };
    }

    private FullScreenMode IndexToMode(int idx)
    {
        return displayModes[idx] switch
        {
            DisplayMode.Windowed => FullScreenMode.Windowed,
            DisplayMode.Borderless => FullScreenMode.FullScreenWindow,
            DisplayMode.Fullscreen => FullScreenMode.ExclusiveFullScreen,
            _ => FullScreenMode.Windowed
        };
    }

    // --- search ---
    private int FindIndexMatching(Resolution target)
    {
        for (int i = 0; i < resolutions.Length; i++)
        {
            var r = resolutions[i];
            if (r.width == target.width && r.height == target.height)
                return i;
        }
        return Mathf.Clamp(resolutions.Length - 1, 0, int.MaxValue);
    }
}
