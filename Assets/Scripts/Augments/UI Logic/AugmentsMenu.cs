using UnityEngine;
using UnityEngine.UI;

public class AugmentsMenu : MonoBehaviour
{
    public GameObject augmentsMenu;
    public GameObject hideShow;
    private bool hide = false;
    private bool augments = false;

    [Header("UI - 3 sloty augmentów")]
    [SerializeField] private Image[] augmentImages;

    private static Sprite[] allSprites;

    private void Awake()
    {
        // load all sprites once (cache)
        if (allSprites == null)
            allSprites = Resources.LoadAll<Sprite>("Sprites/Augments");

        // random sprite
        if (allSprites != null && allSprites.Length > 0 && augmentImages != null)
        {
            foreach (var img in augmentImages)
                AssignRandomSprite(img, avoidSame: false);
        }
    }
    void Start()
    {
        augmentsMenu.SetActive(false);
    }
    public void ActivateAugments()
    {
        if(augments == false)
        {
            augmentsMenu.SetActive(true);
            Cursor.visible = true;
            Time.timeScale = 0f;
        }
    }
    public void HideShowButton()
    {
        if(hide == false)
        {
            hide = true;
            hideShow.SetActive(false);
        }
        else
        {
            hide = false; 
            hideShow.SetActive(true);
        }
    }
    public void ChooseAugment()
    {
        //logic

        //----- hide menu, unpause game -----
        augmentsMenu.SetActive(false);
        Cursor.visible = false;
        Time.timeScale = 1f;
    }
    private void AssignRandomSprite(Image target, bool avoidSame)
    {
        if (target == null || allSprites == null || allSprites.Length == 0) return;

        Sprite current = target.sprite;
        Sprite next = allSprites[Random.Range(0, allSprites.Length)];

        if (avoidSame && allSprites.Length > 1)
        {
            int safety = 8;
            while (next == current && safety-- > 0)
                next = allSprites[Random.Range(0, allSprites.Length)];
        }

        target.sprite = next;
    }
    public void Reroll(Image targetImage)
    {
        if (targetImage == null || allSprites == null || allSprites.Length == 0) return;
        AssignRandomSprite(targetImage, avoidSame: true);
    }
}
