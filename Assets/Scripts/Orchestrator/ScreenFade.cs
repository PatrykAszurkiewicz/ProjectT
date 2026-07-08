using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// Fades the screen to black, THEN loads a scene. Used by the start-of-run menus
// (solo / co-op / continue) so the hand-off into the gameplay scene matches how a run
// begins: on black, before the biome intro. Without it, a synchronous LoadScene leaves
// the menu — or a flash of the main menu under a closing overlay — frozen on screen for
// the whole load hitch. The gameplay scene asserts black in GameOrchestrator.Awake
// (before its first frame), so this overlay hands straight off to it with no flash.
public class ScreenFade : MonoBehaviour
{
    private static ScreenFade _active;

    /// <summary>Fade to black over <paramref name="duration"/> seconds (unscaled), then
    /// load <paramref name="scene"/>. Ignored if a fade is already running, so a
    /// double-click can't stack scene loads.</summary>
    public static void LoadScene(string scene, float duration = 0.35f)
    {
        if (string.IsNullOrEmpty(scene))
        {
            // Nothing sensible to fade into — fall back to a plain load.
            SceneManager.LoadScene(scene);
            return;
        }
        if (_active != null) return;

        var go = new GameObject("ScreenFade");
        _active = go.AddComponent<ScreenFade>();
        _active.StartCoroutine(_active.FadeThenLoad(scene, Mathf.Max(0.01f, duration)));
    }

    private Image _img;

    private void OnDestroy() { if (_active == this) _active = null; }

    private void BuildOverlay()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;              // above every menu / HUD / overlay canvas
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>(); // also swallows clicks while fading

        var imgGO = new GameObject("Black", typeof(RectTransform));
        imgGO.transform.SetParent(transform, false);
        _img = imgGO.AddComponent<Image>();
        _img.color = new Color(0f, 0f, 0f, 0f);
        var rt = _img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private IEnumerator FadeThenLoad(string scene, float duration)
    {
        BuildOverlay();

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;          // menus often sit at timeScale 0
            float a = Mathf.Clamp01(t / duration);
            _img.color = new Color(0f, 0f, 0f, a);
            yield return null;
        }
        _img.color = Color.black;

        // Present one fully-black frame before the synchronous load stalls the main
        // thread, so the screen is already black when the hitch happens.
        yield return null;

        // Unfreeze for the next scene: a menu may have left timeScale at 0, and the
        // gameplay intro uses scaled waits that would otherwise hang on black.
        Time.timeScale = 1f;

        // This object lives in the menu scene and is destroyed by the (single) load; the
        // gameplay scene's own black overlay (asserted in Awake) takes over seamlessly.
        SceneManager.LoadScene(scene);
    }
}
