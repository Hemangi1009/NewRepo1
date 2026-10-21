using UnityEngine;
using UnityEngine.UI;

public class AvatarLobbyUI : MonoBehaviour
{
    [Header("Scene Settings")]
    [Tooltip("Name of your main multiplayer scene to load")]
    [SerializeField] private string mainMultiplayerSceneName = "MainMultiplayerScene";

    [Header("UI Buttons")]
    [Tooltip("Drag your 'Start Game' / 'Enter' button here")]
    [SerializeField] private Button startGameButton;

    [Header("Selection Highlights")]
    [Tooltip("Drag the 'Selected' highlight Image/GameObject for each avatar here, in the SAME order as your 8 avatar buttons (Index 0 to 7). Only the one matching the current selection stays enabled.")]
    [SerializeField] private GameObject[] selectionHighlights = new GameObject[8];

    [Tooltip("Drag your 8 avatar buttons here (Index 0 to 7)")]
    [SerializeField] private Button[] avatarButtons = new Button[8];

    private bool _isEntering;

    private int _selectedIndex = -1; // -1 means nothing selected yet

    private void Start()
    {
        // 1. Lock the Start button on launch
        if (startGameButton != null)
        {
            startGameButton.interactable = false;
        }

        // Reset any previous selection
        _selectedIndex = -1;

        // Nothing is selected yet, so all highlight images start off
        UpdateSelectionHighlights();
    }

    /// <summary>
    /// Call this from your 8 avatar buttons (pass 0, 1, 2... 7)
    /// </summary>
    public void SelectHead(int headIndex)
    {
        if (headIndex < 0 || headIndex >= 8) return;

        _selectedIndex = headIndex;
        PlayerPrefs.SetInt("SelectedAvatarIndex", _selectedIndex);
        PlayerPrefs.Save();

        // Turn off every highlight, then turn on only the one just picked
        UpdateSelectionHighlights();

        // 2. An avatar is now selected! Unlock the Start Game button
        if (startGameButton != null)
        {
            startGameButton.interactable = true;
        }

        Debug.Log($"[Lobby] Selected Avatar {headIndex}! Start button is now active.");
    }

    /// <summary>
    /// Enables the highlight image matching the current selection and disables every other one.
    /// </summary>
    private void UpdateSelectionHighlights()
    {
        if (selectionHighlights == null) return;

        for (int i = 0; i < selectionHighlights.Length; i++)
        {
            if (selectionHighlights[i] != null)
            {
                selectionHighlights[i].SetActive(i == _selectedIndex);
            }
        }
    }

    /// <summary>
    /// Call this from your Start Game button's OnClick
    /// </summary>
    public void EnterMultiplayerScene()
    {
        if (_isEntering) return; //ignore double clicks

        if (_selectedIndex == -1)
        {
            Debug.LogWarning("[Lobby] Please select an avatar first!");
            return;
        }

        if (VRSharedLauncher.Instance == null)
        {
            Debug.LogError("[Lobby] No VRSharedLauncher instance found - is it in the scene? Cannot join multiplayer without it.");
            return;
        }

        _isEntering = true;
        SetButtonInteractable(false); // lock Start + all 8 avatar buttons

        Debug.Log($"[Lobby] Joining shared room and entering '{mainMultiplayerSceneName}' with avatar: {_selectedIndex}...");

        // This connects to Fusion AND loads the multiplayer scene in one step,
        // right now (only after avatar selection) - not earlier. See
        // VRSharedLauncher.JoinAndEnterMultiplayer() for why connecting any
        // earlier would force other still-choosing players into this scene too.
        VRSharedLauncher.Instance.JoinAndEnterMultiplayer(mainMultiplayerSceneName);
    }

    private void SetButtonInteractable(bool value)
    {
        if (startGameButton != null) startGameButton.interactable = value;

        if (avatarButtons == null) return;
        foreach (var b in avatarButtons)
        {
            if (b != null) b.interactable = value;
        }
    }
}