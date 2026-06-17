using UnityEngine;
using System.Collections;


public class WinScreen : MonoBehaviour
{
    private CanvasGroup canvasGroup;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }
    public void HideUI()
    {
        StartCoroutine(FadeOut());
    }
    private IEnumerator FadeOut()
    {
        float elapsed = 0f;

        while (elapsed < 1f)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = 1 - (elapsed / 1f);
            yield return null;
        }

        gameObject.SetActive(false);
    }
}
