using SG;
using Unity.Netcode;
using UnityEngine;

public class TitleScrrenManager : MonoBehaviour
{
    [Header("UI Panels")]
    [Tooltip("The initial panel with the 'Press Start' button.")]
    [SerializeField] private GameObject pressStartPanel;

    [Tooltip("The panel with the 'Host' and 'Join' buttons, shown after Start is pressed.")]
    [SerializeField] private GameObject hostJoinPanel;

    public void OnPressStart()
    {
        pressStartPanel.SetActive(false);
        hostJoinPanel.SetActive(true);
    }

    public void OnHostButton()
    {
        NetworkManager.Singleton.StartHost();
        StartCoroutine(WorldGameManager.instance.LoadNewGame());
    }
    public void OnJoinButton()
    { 
        NetworkManager.Singleton.StartClient();
    }
}