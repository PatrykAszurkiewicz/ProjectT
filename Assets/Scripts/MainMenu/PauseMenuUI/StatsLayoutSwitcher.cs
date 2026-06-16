using UnityEngine;


// Shows the single-player stats panel OR the co-op stats group depending on how
// many players are in the run. Put this on your pause-menu root (an object that
// is active whenever the pause screen is shown), and assign:
//    singlePlayerPanel — your original full StatsPanelUI object
//    coopStatsGroup     — the parent holding the P1 / P2 / Shared panels
// Player count is fixed for a run, but we re-evaluate every time the pause
// screen opens (OnEnable) so it's always correct regardless of spawn timing.

public class StatsLayoutSwitcher : MonoBehaviour
{
    [Tooltip("Your original full stats panel (Player Index -1, all sections).")]
    [SerializeField] private GameObject singlePlayerPanel;

    [Tooltip("Parent object holding the P1 / P2 / Shared co-op panels.")]
    [SerializeField] private GameObject coopStatsGroup;

    private void OnEnable() => Apply();
    private void Start() => Apply();

    private void Apply()
    {
        bool coop = PlayerRegistry.Count > 1;
        if (singlePlayerPanel != null) singlePlayerPanel.SetActive(!coop);
        if (coopStatsGroup != null) coopStatsGroup.SetActive(coop);
    }
}
