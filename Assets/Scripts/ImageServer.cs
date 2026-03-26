using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Python 추론 루프에서 카메라 프레임을 요청할 수 있는 TCP 서버 (port 5008).
///
/// Protocol:
///   Python → Unity: "GET\n"
///   Unity → Python: [4 bytes big-endian: jpg_length][jpg_bytes]
/// </summary>
public class ImageServer : MonoBehaviour
{
    [Header("Network")]
    public int port = 5008;

    [Header("Capture")]
    public Camera captureCamera;
    public int captureWidth  = 224;
    public int captureHeight = 224;
    public int jpegQuality   = 85;

    private TcpListener _listener;
    private Thread      _serverThread;
    private bool        _running;

    // 메인 스레드 ↔ 서버 스레드 동기화
    private volatile bool          _captureRequested = false;
    private byte[]                 _latestJpg        = null;
    private readonly object        _jpgLock          = new object();
    private ManualResetEventSlim   _jpgReady         = new ManualResetEventSlim(false);

    private RenderTexture _rt;
    private Texture2D     _tex;

    void Start()
    {
        _rt  = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
        _tex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);

        _running      = true;
        _serverThread = new Thread(ServerLoop) { IsBackground = true, Name = "ImageServer" };
        _serverThread.Start();
    }

    void OnDestroy()
    {
        _running = false;
        _listener?.Stop();
        if (_rt  != null) _rt.Release();
        if (_tex != null) Destroy(_tex);
    }

    // 카메라 캡처는 반드시 메인 스레드에서
    void Update()
    {
        if (!_captureRequested || captureCamera == null) return;
        _captureRequested = false;

        RenderTexture prevRT     = captureCamera.targetTexture;
        RenderTexture prevActive = RenderTexture.active;

        captureCamera.targetTexture = _rt;
        captureCamera.Render();
        RenderTexture.active = _rt;
        _tex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
        _tex.Apply();

        captureCamera.targetTexture = prevRT;
        RenderTexture.active        = prevActive;

        lock (_jpgLock)
            _latestJpg = _tex.EncodeToJPG(jpegQuality);

        _jpgReady.Set();
    }

    // ── TCP 서버 ─────────────────────────────────────────────────────────

    private void ServerLoop()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            Debug.Log($"[ImageServer] Listening on port {port}");

            while (_running)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                }
                catch (SocketException e)
                {
                    if (_running) Debug.LogWarning($"[ImageServer] Accept: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            if (_running) Debug.LogError($"[ImageServer] Fatal: {e}");
        }
    }

    private void HandleClient(TcpClient client)
    {
        var stream = client.GetStream();
        var buf    = new byte[64];
        var sb     = new StringBuilder();

        while (_running && client.Connected)
        {
            try
            {
                int n = stream.Read(buf, 0, buf.Length);
                if (n == 0) break;

                sb.Append(Encoding.UTF8.GetString(buf, 0, n));

                int nl;
                while ((nl = sb.ToString().IndexOf('\n')) >= 0)
                {
                    string cmd = sb.ToString(0, nl).Trim();
                    sb.Remove(0, nl + 1);

                    if (cmd == "GET")
                    {
                        _jpgReady.Reset();
                        _captureRequested = true;

                        if (!_jpgReady.Wait(2000)) // 최대 2초 대기
                        {
                            Debug.LogWarning("[ImageServer] Capture timeout");
                            continue;
                        }

                        lock (_jpgLock)
                        {
                            if (_latestJpg == null) continue;
                            uint len = (uint)_latestJpg.Length;
                            stream.Write(new byte[]
                            {
                                (byte)(len >> 24), (byte)(len >> 16),
                                (byte)(len >>  8), (byte) len
                            }, 0, 4);
                            stream.Write(_latestJpg, 0, _latestJpg.Length);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (_running) Debug.LogWarning($"[ImageServer] Client: {e.Message}");
                break;
            }
        }
        client.Close();
    }
}
