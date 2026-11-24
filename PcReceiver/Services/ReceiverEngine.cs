using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using PcReceiver.Models;

namespace PcReceiver.Services;

/// <summary>
/// Listens for 14-byte XUSB packets from the Android app and drives a virtual Xbox 360 controller via ViGEmBus.
/// </summary>
public sealed class ReceiverEngine : IDisposable
{
    public const int PacketLength = 14;
    private const byte Magic = 0xA5;
    private const byte ProtocolVersion = 0x01;

    private readonly object _snapshotLock = new();
    private GamepadSnapshot _latestSnapshot;
    private long _totalPackets;

    private ViGEmClient? _vigem;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public GamepadSnapshot LatestSnapshot
    {
        get
        {
            lock (_snapshotLock)
                return _latestSnapshot;
        }
    }

    public long TotalPackets => Interlocked.Read(ref _totalPackets);

    public bool IsRunning { get; private set; }

    public bool IsClientConnected { get; private set; }

    public bool IsVigemMissing { get; private set; }

    public int CurrentPort { get; private set; } = 28555;

    public event Action<string>? LogAdded;

    public event Action? StateChanged;

    public bool Start(int port)
    {
        Stop();

        try
        {
            _vigem ??= new ViGEmClient();
            IsVigemMissing = false;
        }
        catch (VigemBusNotFoundException)
        {
            IsVigemMissing = true;
            EmitLog("ViGEmBus sürücüsü bulunamadı! https://github.com/nefarius/ViGEmBus/releases adresinden kurun.");
            StateChanged?.Invoke();
            return false;
        }
        catch (Exception ex)
        {
            EmitLog($"ViGEm başlatma hatası: {ex.Message}");
            StateChanged?.Invoke();
            return false;
        }

        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            _listener = listener;
        }
        catch (SocketException ex)
        {
            EmitLog($"Port {port} dinlenemedi: {ex.Message}");
            StateChanged?.Invoke();
            return false;
        }

        CurrentPort = port;
        IsRunning = true;
        IsClientConnected = false;
        Interlocked.Exchange(ref _totalPackets, 0);
        lock (_snapshotLock)
        {
            _latestSnapshot = default;
        }

        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;
        _acceptTask = Task.Run(() => AcceptLoopAsync(_listener, _vigem, ct), ct);

        EmitLog($"Sunucu dinlemede (0.0.0.0:{port}).");
        StateChanged?.Invoke();
        return true;
    }

    public void Stop()
    {
        bool wasRunning = IsRunning || _listener is not null;

        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }

        if (_acceptTask is not null)
        {
            try { _acceptTask.Wait(300); } catch { }
            _acceptTask = null;
        }

        _cts?.Dispose();
        _cts = null;
        _listener = null;

        IsRunning = false;
        IsClientConnected = false;
        lock (_snapshotLock)
        {
            _latestSnapshot = default;
        }

        if (wasRunning)
        {
            EmitLog("Sunucu durduruldu.");
        }
        StateChanged?.Invoke();
    }

    private async Task AcceptLoopAsync(TcpListener listener, ViGEmClient vigem, CancellationToken ct)
    {
        var buffer = new byte[PacketLength];

        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                EmitLog($"Bağlantı kabul hatası: {ex.Message}");
                continue;
            }

            using (client)
            {
                client.NoDelay = true;
                IXbox360Controller pad = vigem.CreateXbox360Controller();
                pad.AutoSubmitReport = false;
                pad.Connect();

                IsClientConnected = true;
                string endpoint = client.Client.RemoteEndPoint?.ToString() ?? "Telefon";
                EmitLog($"Telefon bağlandı ({endpoint}) → Sanal Xbox 360 kolu aktif.");
                StateChanged?.Invoke();

                bool badFrameLogged = false;

                try
                {
                    using NetworkStream stream = client.GetStream();
                    while (!ct.IsCancellationRequested)
                    {
                        await stream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);

                        if (buffer[0] != Magic || buffer[1] != ProtocolVersion)
                        {
                            if (!badFrameLogged)
                            {
                                EmitLog($"Geçersiz paket başlığı atlandı (0x{buffer[0]:X2} v{buffer[1]}).");
                                badFrameLogged = true;
                            }
                            continue;
                        }

                        ushort buttons = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(2, 2));
                        byte lt = buffer[4];
                        byte rt = buffer[5];
                        short lx = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(6, 2));
                        short ly = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(8, 2));
                        short rx = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(10, 2));
                        short ry = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(12, 2));

                        pad.SetButtonsFull(buttons);
                        pad.SetSliderValue(Xbox360Slider.LeftTrigger, lt);
                        pad.SetSliderValue(Xbox360Slider.RightTrigger, rt);
                        pad.SetAxisValue(Xbox360Axis.LeftThumbX, lx);
                        pad.SetAxisValue(Xbox360Axis.LeftThumbY, ly);
                        pad.SetAxisValue(Xbox360Axis.RightThumbX, rx);
                        pad.SetAxisValue(Xbox360Axis.RightThumbY, ry);
                        pad.SubmitReport();

                        lock (_snapshotLock)
                        {
                            _latestSnapshot = new GamepadSnapshot(buttons, lt, rt, lx, ly, rx, ry);
                        }
                        Interlocked.Increment(ref _totalPackets);
                    }
                }
                catch (Exception ex) when (ex is EndOfStreamException or IOException or OperationCanceledException)
                {
                    EmitLog("Telefon bağlantısı kesildi.");
                }
                catch (Exception ex)
                {
                    EmitLog($"Akış hatası: {ex.Message}");
                }
                finally
                {
                    lock (_snapshotLock)
                    {
                        _latestSnapshot = default;
                    }
                    IsClientConnected = false;

                    try
                    {
                        pad.ResetReport();
                        pad.SubmitReport();
                        pad.Disconnect();
                    }
                    catch
                    {
                        // Sürücü kapanışı sırasındaki hataları yoksay.
                    }

                    StateChanged?.Invoke();
                }
            }
        }
    }

    public static IReadOnlyList<string> GetLocalIPv4Addresses()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                .Select(ip => ip.ToString())
                .Distinct()
                .OrderByDescending(ip => ip.StartsWith("192.168.", StringComparison.Ordinal))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static async Task<(bool Ok, string Message)> RunAdbReverseAsync(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = $"reverse tcp:{port} tcp:{port}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (false, "adb işlemi başlatılamadı.");

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return (false, "adb komutu zaman aşımına uğradı (5 sn).");
            }

            if (process.ExitCode == 0)
                return (true, $"ADB tüneli hazır (tcp:{port}) → Telefonda 127.0.0.1 kullanın.");

            string err = (await process.StandardError.ReadToEndAsync().ConfigureAwait(false)).Trim();
            string stdout = (await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false)).Trim();
            string detail = !string.IsNullOrEmpty(err) ? err : stdout;
            return (false, string.IsNullOrEmpty(detail) ? $"adb hata kodu: {process.ExitCode}" : detail);
        }
        catch (Win32Exception)
        {
            return (false, "adb bulunamadı — Android platform-tools kurup PATH'e ekleyin.");
        }
        catch (Exception ex)
        {
            return (false, $"adb hatası: {ex.Message}");
        }
    }

    private void EmitLog(string message)
    {
        LogAdded?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    public void Dispose()
    {
        Stop();
        _vigem?.Dispose();
        _vigem = null;
    }
}
