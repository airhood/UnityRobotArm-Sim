using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class IKReceiverSettings
{
    public static string serverHost;
    public static int serverPort;
}

public static class ManipulatorModeSetting
{
    public static Manipulator.ControlMode controlMode;
}

public class SystemStart : MonoBehaviour
{
    [SerializeField] private TMP_InputField serverHostInputField;
    [SerializeField] private TMP_InputField serverPortInputField;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button manualButton;
    
    void Start()
    {
        connectButton.onClick.AddListener(Connect);
        manualButton.onClick.AddListener(Manual);
    }

    private void OnDestroy()
    {
        connectButton.onClick.RemoveListener(Connect);
        manualButton.onClick.RemoveListener(Manual);
    }

    public void Connect()
    {
        string serverHost = serverHostInputField.text;
        string serverPort = serverPortInputField.text;
    
        // Host 검사
        if (string.IsNullOrWhiteSpace(serverHost))
        {
            Debug.LogWarning("Host가 비어있습니다.");
            return;
        }
    
        // Port 검사
        if (string.IsNullOrWhiteSpace(serverPort))
        {
            Debug.LogWarning("Port가 비어있습니다.");
            return;
        }
    
        if (!int.TryParse(serverPort, out int port) || port < 1 || port > 65535)
        {
            Debug.LogWarning($"Port 형식이 올바르지 않습니다: {serverPort} (1~65535 사이 숫자)");
            return;
        }
    
        // Host 형식 검사 (IP 또는 도메인)
        if (!IsValidHost(serverHost))
        {
            Debug.LogWarning($"Host 형식이 올바르지 않습니다: {serverHost}");
            return;
        }
    
        IKReceiverSettings.serverHost = serverHost;
        IKReceiverSettings.serverPort = port;
        ManipulatorModeSetting.controlMode = Manipulator.ControlMode.IKReceiver;
        SceneManager.LoadScene("MainScene");
    }

    private bool IsValidHost(string host)
    {
        // IP 주소 체크 (예: 192.168.0.1)
        if (System.Net.IPAddress.TryParse(host, out _))
            return true;
    
        // 도메인 체크 (예: example.com, localhost)
        var domainRegex = new System.Text.RegularExpressions.Regex(
            @"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)*[a-zA-Z0-9][a-zA-Z0-9\-]{0,61}[a-zA-Z]$"
        );
        return host == "localhost" || domainRegex.IsMatch(host);
    }

    public void Manual()
    {
        ManipulatorModeSetting.controlMode = Manipulator.ControlMode.Manual;
        SceneManager.LoadScene("MainScene");
    }
}
