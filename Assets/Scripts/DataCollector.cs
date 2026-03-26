using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Connects to the Python DataCollectorServer (port 5006) and streams
/// camera frames + robot state for training data collection.
///
/// Wire format per frame (big-endian uint32 length-prefixed):
///   [4 bytes: json_len][json_bytes][4 bytes: jpg_len][jpg_bytes]
/// </summary>
public class DataCollector : MonoBehaviour
{
    [Header("Network")]
    public string serverHost = "127.0.0.1";
    public int serverPort = 5006;

    [Header("Capture")]
    public Camera captureCamera;
    public int captureWidth = 224;
    public int captureHeight = 224;
    [Range(1, 60)]
    public int captureEveryNFrames = 3;
    public int jpegQuality = 85;

    [Header("References")]
    public IKReceiver ikReceiver;
    public SliderGripper gripper;

    // ── Episode tracking (set by PickPlaceDemo) ──────────────────────────
    [HideInInspector] public int    currentEpisodeId    = -1;
    [HideInInspector] public string currentEpisodeLabel = "";
    [HideInInspector] public string currentPhase        = "";

    public void BeginEpisode(int id, string label)
    {
        currentEpisodeId    = id;
        currentEpisodeLabel = label;
        currentPhase        = "";
        Debug.Log($"[DataCollector] Episode {id} start: '{label}'");
    }

    public void EndEpisode()
    {
        Debug.Log($"[DataCollector] Episode {currentEpisodeId} end");
        currentEpisodeId    = -1;
        currentEpisodeLabel = "";
        currentPhase        = "";
    }

    public void SetPhase(string phase) => currentPhase = phase;

    private TcpClient _client;
    private NetworkStream _stream;
    private readonly object _streamLock = new object();
    private bool _connected;
    private Thread _connectThread;
    private bool _running;
    private int _frameCount;

    private RenderTexture _rt;
    private Texture2D _tex;

    void Start()
    {
        _rt = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
        _tex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        _running = true;
        _connectThread = new Thread(ConnectLoop) { IsBackground = true, Name = "DataCollector" };
        _connectThread.Start();
    }

    void OnDestroy()
    {
        _running = false;
        _client?.Close();
        if (_rt != null) _rt.Release();
        if (_tex != null) Destroy(_tex);
    }

    void Update()
    {
        // 에피소드가 활성화되지 않은 구간은 전송하지 않음
        if (!_connected || captureCamera == null || currentEpisodeId < 0) return;

        _frameCount++;
        if (_frameCount % captureEveryNFrames != 0) return;

        // Capture from dedicated camera (main thread only)
        RenderTexture prevRT = captureCamera.targetTexture;
        RenderTexture prevActive = RenderTexture.active;

        captureCamera.targetTexture = _rt;
        captureCamera.Render();
        RenderTexture.active = _rt;
        _tex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
        _tex.Apply();

        captureCamera.targetTexture = prevRT;
        RenderTexture.active = prevActive;

        byte[] jpg = _tex.EncodeToJPG(jpegQuality);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(BuildStateJson());

        // Send in thread pool to avoid blocking main thread
        ThreadPool.QueueUserWorkItem(_ => SendFrame(jsonBytes, jpg));
    }

    private string BuildStateJson()
    {
        var ic = CultureInfo.InvariantCulture;

        float[] j = ikReceiver?.LastJointAngles;
        string jointsArr = j != null
            ? "[" + string.Join(",", Array.ConvertAll(j, v => v.ToString("F3", ic))) + "]"
            : "[0,0,0,0,0]";

        Vector3 ee  = ikReceiver?.endPoint != null ? ikReceiver.endPoint.position : Vector3.zero;
        Vector3 tgt = ikReceiver?.target   != null ? ikReceiver.target.position   : Vector3.zero;
        float gripAngle = gripper?.angleJoint != null ? gripper.angleJoint.GetAnglePosition() : 0f;
        double ts = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        return "{" +
            "\"timestamp\":"     + ts.ToString("F3", ic)          + "," +
            "\"episode_id\":"    + currentEpisodeId                + "," +
            "\"episode_label\":\"" + EscapeJson(currentEpisodeLabel) + "\"," +
            "\"phase\":\""       + EscapeJson(currentPhase)        + "\"," +
            "\"joint_angles\":"  + jointsArr                       + "," +
            "\"ee_position\":["  + ee.x.ToString("F4", ic)  + "," + ee.y.ToString("F4", ic)  + "," + ee.z.ToString("F4", ic)  + "]," +
            "\"target_position\":[" + tgt.x.ToString("F4", ic) + "," + tgt.y.ToString("F4", ic) + "," + tgt.z.ToString("F4", ic) + "]," +
            "\"gripper_angle\":"  + gripAngle.ToString("F2", ic) +
            "}";
    }

    private void SendFrame(byte[] jsonBytes, byte[] jpgBytes)
    {
        lock (_streamLock)
        {
            if (!_connected || _stream == null) return;
            try
            {
                WriteUInt32BE(_stream, (uint)jsonBytes.Length);
                _stream.Write(jsonBytes, 0, jsonBytes.Length);
                WriteUInt32BE(_stream, (uint)jpgBytes.Length);
                _stream.Write(jpgBytes, 0, jpgBytes.Length);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DataCollector] Send error: {e.Message}");
                _connected = false;
                _client?.Close();
            }
        }
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n",  "\\n")
                .Replace("\r",  "\\r")
                .Replace("\t",  "\\t");
    }

    private void WriteUInt32BE(NetworkStream s, uint v)
    {
        s.Write(new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }, 0, 4);
    }

    private void ConnectLoop()
    {
        while (_running)
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(serverHost, serverPort);
                _stream = _client.GetStream();
                _connected = true;
                Debug.Log($"[DataCollector] Connected to Python at {serverHost}:{serverPort}");

                while (_running && _connected) Thread.Sleep(200);
            }
            catch (Exception e)
            {
                if (_running) Debug.LogWarning($"[DataCollector] Connect failed: {e.Message}. Retrying in 2s...");
                _connected = false;
            }
            if (_running) Thread.Sleep(2000);
        }
    }
}
