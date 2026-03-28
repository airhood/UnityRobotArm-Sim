using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// TCP server that accepts commands from the Python AI model.
/// Unity listens on port 5007; Python connects to it.
///
/// Protocol (newline-delimited ASCII):
///   Python → Unity:
///     MOVE_TO x,y,z       - move target to world position
///     MOVE_REL dx,dy,dz   - move target by offset
///     GRIPPER open|close  - set gripper state
///     GET_STATE           - request current robot state
///   Unity → Python:
///     OK
///     FAIL
///     STATE j0,j1,j2,j3,j4,ex,ey,ez,tx,ty,tz
/// </summary>
public class AICommandServer : MonoBehaviour
{
    [Header("Network")]
    public int port = 5007;

    [Header("References")]
    public Transform targetTransform;
    public Manipulator manipulator;
    public IKReceiver ikReceiver;

    private ConcurrentQueue<Action> _mainQueue = new ConcurrentQueue<Action>();
    private TcpListener _listener;
    private TcpClient _currentClient;
    private NetworkStream _currentStream;
    private readonly object _sendLock = new object();
    private Thread _serverThread;
    private bool _running;

    void Start()
    {
        _running = true;
        _serverThread = new Thread(ServerLoop) { IsBackground = true, Name = "AICommandServer" };
        _serverThread.Start();
    }

    void OnDestroy()
    {
        _running = false;
        _listener?.Stop();
        _currentClient?.Close();
    }

    void Update()
    {
        while (_mainQueue.TryDequeue(out Action act))
            act?.Invoke();
    }

    private void ServerLoop()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            Debug.Log($"[AICommandServer] Listening on port {port}");

            while (_running)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    Debug.Log("[AICommandServer] Python AI connected");
                    _currentClient = client;
                    _currentStream = client.GetStream();
                    HandleClient(client);
                }
                catch (SocketException e)
                {
                    if (_running) Debug.LogWarning($"[AICommandServer] Accept: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            if (_running) Debug.LogError($"[AICommandServer] Fatal: {e}");
        }
    }

    private void HandleClient(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buf = new byte[4096];
        StringBuilder sb = new StringBuilder();

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
                    string line = sb.ToString(0, nl).Trim();
                    sb.Remove(0, nl + 1);
                    if (line.Length > 0) DispatchCommand(line, stream);
                }
            }
            catch (Exception e)
            {
                if (_running) Debug.LogWarning($"[AICommandServer] Read: {e.Message}");
                break;
            }
        }

        client.Close();
        Debug.Log("[AICommandServer] Client disconnected");
    }

    private void DispatchCommand(string line, NetworkStream stream)
    {
        string[] parts = line.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToUpper();

        switch (cmd)
        {
            case "MOVE_TO":
                if (parts.Length >= 2 && TryParseVec3(parts[1], out float x, out float y, out float z))
                {
                    _mainQueue.Enqueue(() =>
                    {
                        if (targetTransform != null) targetTransform.position = new Vector3(x, y, z);
                        Send(stream, "OK\n");
                    });
                }
                else Send(stream, "FAIL\n");
                break;

            case "MOVE_REL":
                if (parts.Length >= 2 && TryParseVec3(parts[1], out float dx, out float dy, out float dz))
                {
                    _mainQueue.Enqueue(() =>
                    {
                        if (targetTransform != null) targetTransform.position += new Vector3(dx, dy, dz);
                        Send(stream, "OK\n");
                    });
                }
                else Send(stream, "FAIL\n");
                break;

            case "GRIPPER":
                if (parts.Length >= 2)
                {
                    bool open = parts[1].ToLower().StartsWith("open");
                    _mainQueue.Enqueue(() =>
                    {
                        manipulator.SetGripperOpen(open);
                        Send(stream, "OK\n");
                    });
                }
                else Send(stream, "FAIL\n");
                break;

            case "GET_STATE":
                _mainQueue.Enqueue(() => Send(stream, BuildState()));
                break;

            default:
                Send(stream, "FAIL\n");
                break;
        }
    }

    private string BuildState()
    {
        float[] j = manipulator.GetCurrentJointAngles();
        string joints = string.Join(",", Array.ConvertAll(j, a => a.ToString("F2")));

        Vector3 ee  = manipulator.endPoint.position;
        Vector3 tgt = targetTransform != null ? targetTransform.position : Vector3.zero;

        return $"STATE {joints},{ee.x:F4},{ee.y:F4},{ee.z:F4},{tgt.x:F4},{tgt.y:F4},{tgt.z:F4}\n";
    }

    private void Send(NetworkStream stream, string msg)
    {
        lock (_sendLock)
        {
            try
            {
                byte[] d = Encoding.UTF8.GetBytes(msg);
                stream?.Write(d, 0, d.Length);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AICommandServer] Send: {e.Message}");
            }
        }
    }

    private bool TryParseVec3(string s, out float x, out float y, out float z)
    {
        x = y = z = 0f;
        string[] p = s.Split(',');
        if (p.Length != 3) return false;
        return float.TryParse(p[0].Trim(), out x)
            && float.TryParse(p[1].Trim(), out y)
            && float.TryParse(p[2].Trim(), out z);
    }
}
