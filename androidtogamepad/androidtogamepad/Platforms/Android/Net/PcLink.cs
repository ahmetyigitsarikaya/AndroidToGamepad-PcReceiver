using System.Net.Sockets;

namespace androidtogamepad.Platforms.Android.Net;

/// <summary>
/// Maintains a low-latency TCP connection to PcReceiver and always sends the latest gamepad packet.
/// </summary>
public sealed class PcLink
{
    public static PcLink Instance { get; } = new();

    private readonly object _lifecycleSync = new();
    private readonly AutoResetEvent _signal = new(false);

    private volatile byte[]? _latest;
    private CancellationTokenSource? _cts;
    private Thread? _worker;

    private PcLink()
    {
    }

    public event Action<string>? StatusChanged;

    public string CurrentStatus { get; private set; } = "Bağlantı kapalı";

    public bool IsConnected { get; private set; }

    public void Start(string host, int port)
    {
        Stop();

        lock (_lifecycleSync)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _worker = new Thread(() => RunLoop(host, port, token))
            {
                IsBackground = true,
                Name = "PcLinkWorker"
            };
            _worker.Start();
        }
    }

    public void Send(byte[] packet)
    {
        _latest = packet;
        _signal.Set();
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Thread? worker;

        lock (_lifecycleSync)
        {
            cts = _cts;
            worker = _worker;
            _cts = null;
            _worker = null;
        }

        IsConnected = false;

        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
            _signal.Set();
            try { worker?.Join(500); } catch { }
            cts.Dispose();
        }

        UpdateStatus("Bağlantı kapalı");
    }

    private void RunLoop(string host, int port, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                UpdateStatus($"PC'ye bağlanılıyor {host}:{port}...");
                client = new TcpClient { NoDelay = true };

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                connectCts.CancelAfter(2000);
                client.ConnectAsync(host, port, connectCts.Token).AsTask().GetAwaiter().GetResult();

                IsConnected = true;
                UpdateStatus("PC'ye bağlandı");

                using NetworkStream stream = client.GetStream();
                while (!token.IsCancellationRequested)
                {
                    _signal.WaitOne(250);
                    if (token.IsCancellationRequested)
                        break;

                    byte[]? packet = _latest;
                    if (packet is not null)
                    {
                        stream.Write(packet, 0, packet.Length);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                string message = ex is OperationCanceledException ? "Zaman aşımı (2 sn)" : ex.Message;
                UpdateStatus($"Bağlantı yok, 1 sn sonra tekrar denenecek: {message}");
                try { client?.Dispose(); } catch { }
                token.WaitHandle.WaitOne(1000);
            }
            finally
            {
                IsConnected = false;
                try { client?.Dispose(); } catch { }
            }
        }
    }

    private void UpdateStatus(string status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke(status);
    }
}
