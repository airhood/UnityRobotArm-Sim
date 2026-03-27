using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System;

public class IKReceiver : MonoBehaviour
{
    TcpClient client;
    NetworkStream stream;
    StreamReader reader;
    Thread receiveThread;
    bool running = true;

    public Manipulator manipulator;
    public string serverHost = "127.0.0.1";
    public int serverPort = 5005;
    public float reconnectDelay = 2f;

    volatile float[] latestAngles;
    public float[] LastJointAngles => latestAngles;
    volatile bool isConnected = false;

    public float sendInterval = 0.02f;
    private float sendTimer = 0f;
    private Vector3 lastSentPosition = Vector3.positiveInfinity;
    public float positionThreshold = 0.0001f;

    public Transform target;
    public Transform endPoint;

    private float[] _lastSyncAngles;

    public void StartReceiver()
    {
        receiveThread = new Thread(ReceiveLoop);
        receiveThread.Start();
    }

    void ConnectToServer()
    {
        try
        {
            Debug.Log($"Attempting to connect to {serverHost}:{serverPort}...");
            
            client = new TcpClient();
            client.NoDelay = true;
            client.Connect(serverHost, serverPort);
            
            stream = client.GetStream();
            reader = new StreamReader(stream);
            
            isConnected = true;
            Debug.Log("Connected to server!");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Connection failed: {e.Message}");
            CleanupConnection();
        }
    }

    void CleanupConnection()
    {
        isConnected = false;
        
        try
        {
            if (reader != null)
            {
                reader.Close();
                reader = null;
            }
        }
        catch { }
        
        try
        {
            if (stream != null)
            {
                stream.Close();
                stream = null;
            }
        }
        catch { }
        
        try
        {
            if (client != null)
            {
                client.Close();
                client = null;
            }
        }
        catch { }
    }

    void ReceiveLoop()
    {
        while (running)
        {
            if (!isConnected || client == null || !client.Connected)
            {
                CleanupConnection();
                ConnectToServer();
                
                if (!isConnected)
                {
                    Thread.Sleep((int)(reconnectDelay * 1000));
                    continue;
                }
            }

            try
            {
                string data = reader.ReadLine();
                
                if (data == null)
                {
                    Debug.LogWarning("Connection closed by server");
                    CleanupConnection();
                    continue;
                }
                
                if (string.IsNullOrWhiteSpace(data))
                {
                    Debug.LogWarning("Received empty line, skipping...");
                    continue;
                }
                
                Debug.Log($"Received IK Data: {data}");

                string[] values = data.Split(',');
                float[] angles = new float[values.Length];
                bool parseSuccess = true;
                
                for (int i = 0; i < values.Length; i++)
                {
                    if (float.TryParse(values[i].Trim(), out float angle))
                    {
                        angles[i] = angle;
                    }
                    else
                    {
                        Debug.LogError($"Failed to parse angle at index {i}: '{values[i]}'");
                        parseSuccess = false;
                        break;
                    }
                }

                angles[0] *= -1;

                if (parseSuccess)
                {
                    latestAngles = angles;
                }
            }
            catch (IOException e)
            {
                Debug.LogError($"IO Error: {e.Message}");
                CleanupConnection();
            }
            catch (Exception e)
            {
                Debug.LogError($"Receive error: {e.Message}");
                CleanupConnection();
            }
        }
        
        Debug.Log("Receive thread ended");
    }

    void Update()
    {
        if (manipulator.controlMode != Manipulator.ControlMode.IKReceiver) return;

        if (isConnected && target != null && stream != null)
        {
            Vector3 targetPos = target.position;
            Vector3 manipulatorPos = manipulator.transform.position;
            Vector3 relativePos = targetPos - manipulatorPos;
            bool positionChanged = Vector3.Distance(relativePos, lastSentPosition) > positionThreshold;

            sendTimer += Time.deltaTime;
            if (positionChanged && sendTimer >= sendInterval)
            {
                sendTimer = 0f;
                lastSentPosition = relativePos;

                string message = $"{relativePos.x:F4},{relativePos.y:F4},{relativePos.z:F4}\n";
                try
                {
                    byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
                    stream.Write(data, 0, data.Length);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Send failed: {e.Message}");
                    CleanupConnection();
                }
            }
        }
        
        if (latestAngles == null) return;

        List<ActuatorInstance> actuatorInstances = manipulator.GetActuatorInstances();

        // ── Joint Synchronization ────────────────────────────────────────────
        // IK 타겟이 새 위치로 이동할 때 각 관절의 이동 각도 비율에 맞게
        // profileVelocity를 스케일링하여 모든 관절이 동시에 도달하도록 한다.
        if (manipulator.syncJoints)
        {
            bool triggered = _lastSyncAngles == null;
            if (!triggered && _lastSyncAngles != null)
            {
                float maxChange = 0f;
                for (int i = 0; i < latestAngles.Length && i < _lastSyncAngles.Length; i++)
                    maxChange = Mathf.Max(maxChange, Mathf.Abs(latestAngles[i] - _lastSyncAngles[i]));
                triggered = maxChange > manipulator.syncTriggerDelta;
            }

            if (triggered)
            {
                _lastSyncAngles = (float[])latestAngles.Clone();

                float maxDelta = 0f;
                float[] deltas = new float[actuatorInstances.Count];
                for (int i = 0; i < actuatorInstances.Count && i < latestAngles.Length; i++)
                {
                    float d = Mathf.Abs(latestAngles[i] - actuatorInstances[i].GetPosition());
                    if (d > 180f) d = 360f - d;
                    deltas[i] = d;
                    if (d > maxDelta) maxDelta = d;
                }

                if (maxDelta > 0.5f)
                {
                    for (int i = 0; i < actuatorInstances.Count; i++)
                    {
                        if (actuatorInstances[i] is IActuatorProfile<DynamixelProfile> p)
                        {
                            float scale = deltas[i] / maxDelta;
                            var prof = p.GetProfile();
                            prof.profileVelocity     = Mathf.Max(1, Mathf.RoundToInt(manipulator.profileVelocity     * scale));
                            prof.profileAcceleration = Mathf.Max(1, Mathf.RoundToInt(manipulator.profileAcceleration * scale));
                            p.SetProfile(prof);
                        }
                    }
                }
            }
        }
        // ────────────────────────────────────────────────────────────────────

        for (int i = 0; i < actuatorInstances.Count && i < latestAngles.Length; i++)
        {
            actuatorInstances[i].SetPosition(latestAngles[i]);
        }
    }

    void OnApplicationQuit()
    {
        running = false;
        
        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(2000);
        }
        
        CleanupConnection();
    }

    void OnGUI()
    {
        float width = 350;
        float x = Screen.width - width - 10;

        GUI.Label(new Rect(x, 10, width, 20), 
            isConnected ? "Status: Connected" : "Status: Disconnected");

        if (target && endPoint)
        {
            Vector3 t = target.position;
            Vector3 e = endPoint.position;
            Vector3 diff = e - t;
            float dist = diff.magnitude * 1000f;

            GUI.Label(new Rect(x, 30, width, 20), $"Target:   ({t.x:F4}, {t.y:F4}, {t.z:F4})");
            GUI.Label(new Rect(x, 50, width, 20), $"EndPoint: ({e.x:F4}, {e.y:F4}, {e.z:F4})");
            GUI.Label(new Rect(x, 70, width, 20), $"Diff:     ({diff.x:F4}, {diff.y:F4}, {diff.z:F4})");
            GUI.Label(new Rect(x, 90, width, 20), $"Distance: {dist:F1} mm");
        }
    }
}