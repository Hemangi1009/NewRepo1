using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NamePanelController : MonoBehaviour
{
    [Header("Panel 1 - Name Entry")]
    public GameObject namePanel;
    public TMP_InputField nameInputField;
    public Button nextButton;

    [Header("Panel 2 - Next Panel")]
    public GameObject nextPanel;

    void Start()
    {
        nextButton.interactable = false;

        nameInputField.onValueChanged.AddListener(OnNameChanged);
        nextButton.onClick.AddListener(OnNextClicked);
    }

    void OnNameChanged(string value)
    {
        nextButton.interactable = !string.IsNullOrWhiteSpace(value);
    }

    void OnNextClicked()
    {
        string playerName = nameInputField.text.Trim();

        PlayerPrefs.SetString("PlayerName", playerName);
        PlayerPrefs.Save();

        namePanel.SetActive(false);
        nextPanel.SetActive(true);
    }
}