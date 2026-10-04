using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace LoupixDeck.Plugin.Argus;

/// <summary>
/// Reads sensor data from Argus Monitor's shared-memory data API.
///
/// Layout (from argus_monitor_data_api.h, #pragma pack(1)):
///   ArgusMonitorData header  =  240 bytes
///     +  0  Signature                    u32
///     +  4  ArgusMajor/MinorA/MinorB/Extra  4 * u8
///     +  8  ArgusBuild                   u32
///     + 12  Version                      u32
///     + 16  CycleCounter                 u32
///     + 20  OffsetForSensorType[27]      27 * u32
///     +128  SensorCount[27]              27 * u32
///     +236  TotalSensorCount             u32
///   Each ArgusMonitorSensorData         = 212 bytes
///     +  0  SensorType                   u32
///     +  4  Label                        wchar_t[64]   (128 bytes UTF-16)
///     +132  UnitString                   wchar_t[32]   ( 64 bytes UTF-16)
///     +196  Value                        f64
///     +204  DataIndex                    u32
///     +208  SensorIndex                  u32
/// </summary>
public sealed class ArgusMonitorService : IDisposable
{
    private const string MappingName = "Global\\ARGUSMONITOR_DATA_INTERFACE";
    private const string MutexName = "Global\\ARGUSMONITOR_DATA_INTERFACE_MUTEX";
    private const string ProcessName = "ArgusMonitor";

    private const int SensorEntrySize = 212;
    private const int MaxSensorCount = 512;

    private const int OffsetCycleCounter = 16;
    private const int OffsetTotalSensorCount = 236;
    private const int OffsetSensorData = 240;

    // Reconnect cadence when Argus Monitor isn't running.
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);
    // Poll cadence when connected — only a 4-byte read happens when CycleCounter is unchanged.
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(250);
    // Argus bumps CycleCounter about once a second. Our open handle keeps the mapping alive after
    // Argus exits, so a counter that stops moving this long means Argus is gone.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(15);

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private Mutex? _mutex;

    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    // Null until the first snapshot after (re)connecting, so a first counter value of 0 still counts.
    private uint? _lastCycleCounter;
    private long _lastChangeTicks;

    // Sensor entries copied out of the section, so parsing happens after the mutex is released.
    private readonly byte[] _buffer = new byte[MaxSensorCount * SensorEntrySize];

    private volatile IReadOnlyList<ArgusSensor> _sensors = Array.Empty<ArgusSensor>();

    private volatile ArgusDiagnostics _status = new("Not started", []);
    private volatile ArgusDiagnostics? _lastError;

    public IReadOnlyList<ArgusSensor> Sensors => _sensors;
    public bool IsAvailable => _accessor != null;

    /// <summary>What the service is doing right now, as a format string and its arguments so the
    /// plugin can translate it.</summary>
    public ArgusDiagnostics Status => _status;

    /// <summary>The most recent problem, kept after the status has moved on; null if none yet.</summary>
    public ArgusDiagnostics? LastError => _lastError;

    private void SetStatus(string format, params object[] args) => _status = new ArgusDiagnostics(format, args);

    private void SetError(string format, params object[] args)
    {
        ArgusDiagnostics error = new(format, args);
        _lastError = error;
        Console.WriteLine($"ArgusMonitorService: {error.Text}");
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            SetStatus("Argus Monitor is only available on Windows.");
            return;
        }
        if (_pollTask != null)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        // The poll task owns the handles and closes them itself once it has ended, so Stop() never
        // disposes them under a snapshot that is still running.
        _pollTask = Task.Run(async () =>
        {
            try { await PollLoop(token).ConfigureAwait(false); }
            finally { Close(); }
        }, token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _pollTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _pollTask = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!IsAvailable)
            {
                if (!TryOpen())
                {
                    try { await Task.Delay(ReconnectDelay, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    continue;
                }
                _lastCycleCounter = null;
                _lastChangeTicks = Environment.TickCount64;
            }

            try
            {
                if (TrySnapshot(out var snapshot))
                {
                    _sensors = snapshot!;
                    _lastChangeTicks = Environment.TickCount64;
                    SetStatus("Reading — {0} sensor(s).", snapshot!.Count);
                }
                else if (Environment.TickCount64 - _lastChangeTicks > StaleAfter.TotalMilliseconds)
                {
                    SetStatus("No new data from Argus Monitor — reconnecting.");
                    SetError("No new data from Argus Monitor for {0} s.", (int)StaleAfter.TotalSeconds);
                    Close();
                    continue;
                }
            }
            catch (Exception ex)
            {
                SetStatus("Reading failed — reconnecting.");
                SetError("Reading failed ({0}: {1}).", ex.GetType().Name, ex.Message);
                Close();
            }

            try { await Task.Delay(PollDelay, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private bool TryOpen()
    {
        try
        {
            _mmf = MemoryMappedFile.OpenExisting(MappingName, MemoryMappedFileRights.Read);
            // Length 0 maps the entire section, whatever size Argus created it with.
            _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            // The mutex may not yet exist if Argus is mid-startup; treat that as not-available.
            _mutex = Mutex.OpenExisting(MutexName);
            SetStatus("Connected — waiting for data.");
            return true;
        }
        catch (FileNotFoundException)
        {
            SetStatus(MappingMissing());
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            SetStatus(StartingUp);
        }
        catch (UnauthorizedAccessException ex)
        {
            SetStatus(AccessDenied);
            SetError("Access denied ({0}).", ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("Cannot open Argus Monitor's data — retrying.");
            SetError("Opening failed ({0}: {1}).", ex.GetType().Name, ex.Message);
        }

        Close();
        return false;
    }

    // Why the data API cannot be opened; English keys of the plugin's strings files.
    private const string NotRunning = "Not running — is Argus Monitor open?";
    private const string DataApiOff =
        "Argus Monitor is running, but its data API is off — turn on 'Enable Argus Monitor Data API' in its settings.";
    private const string StartingUp = "Argus Monitor is starting — its data API is not ready yet.";
    private const string AccessDenied =
        "Access to Argus Monitor's data was denied — start LoupixDeck with the same rights as Argus Monitor.";

    // No mapping: either Argus is not running, or it runs with its data API turned off.
    private static string MappingMissing() => IsArgusRunning() ? DataApiOff : NotRunning;

    private static bool IsArgusRunning()
    {
        Process[] processes = Process.GetProcessesByName(ProcessName);
        foreach (Process process in processes)
            process.Dispose();
        return processes.Length > 0;
    }

    private void Close()
    {
        try { _accessor?.Dispose(); } catch { }
        try { _mmf?.Dispose(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        _accessor = null;
        _mmf = null;
        _mutex = null;
        _sensors = Array.Empty<ArgusSensor>();
    }

    private bool TrySnapshot(out IReadOnlyList<ArgusSensor>? sensors)
    {
        sensors = null;

        if (!TryCopySensorData(out var count))
            return false;

        var list = new List<ArgusSensor>(count);
        for (var i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = _buffer.AsSpan(i * SensorEntrySize, SensorEntrySize);

            var type = (ArgusSensorType)BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(0, 4));
            var label = ReadWideString(entry.Slice(4, 128));
            var unit = ReadWideString(entry.Slice(132, 64));
            var value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(entry.Slice(196, 8)));
            var dataIndex = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(204, 4));
            var sensorIndex = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(208, 4));

            list.Add(new ArgusSensor(type, label, unit, value, dataIndex, sensorIndex));
        }

        AppendComputedSensors(list);

        sensors = list;
        return true;
    }

    /// <summary>
    /// Copies the sensor entries into <see cref="_buffer"/> while holding Argus's mutex. The lock
    /// covers only the copy, so Argus's writer is not blocked while we decode strings.
    /// </summary>
    private unsafe bool TryCopySensorData(out int count)
    {
        count = 0;

        if (_accessor == null || _mutex == null)
            return false;

        var acquired = false;
        try
        {
            acquired = _mutex.WaitOne(TimeSpan.FromMilliseconds(500), false);
            if (!acquired)
                return false;

            byte* basePtr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
            try
            {
                if (basePtr == null)
                    return false;

                // Every read below stays inside the mapped section.
                var capacity = (int)Math.Min(_accessor.Capacity, int.MaxValue);
                if (capacity < OffsetSensorData)
                    return false;

                var view = new ReadOnlySpan<byte>(basePtr, capacity);

                var cycleCounter = BinaryPrimitives.ReadUInt32LittleEndian(view.Slice(OffsetCycleCounter, 4));
                if (cycleCounter == _lastCycleCounter)
                    return false;

                var totalSensorCount = BinaryPrimitives.ReadUInt32LittleEndian(view.Slice(OffsetTotalSensorCount, 4));
                var fitCount = (uint)((capacity - OffsetSensorData) / SensorEntrySize);
                totalSensorCount = Math.Min(totalSensorCount, Math.Min(MaxSensorCount, fitCount));

                count = (int)totalSensorCount;
                view.Slice(OffsetSensorData, count * SensorEntrySize).CopyTo(_buffer);

                _lastCycleCounter = cycleCounter;
                return true;
            }
            finally
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died while holding the mutex. The wait still granted it to us, so
            // the finally block must release it; skip this one snapshot.
            acquired = true;
            return false;
        }
        finally
        {
            if (acquired)
            {
                try { _mutex.ReleaseMutex(); } catch { }
            }
        }
    }

    // Argus exposes the CPU bus/FSB clock and the per-core multiplier separately.
    // The actual core frequency is FSB × multiplier — synthesize that as a virtual sensor
    // so it shows up in the editor menu and Argus.Sensor(CpuFrequency:N) can read it.
    private static void AppendComputedSensors(List<ArgusSensor> list)
    {
        var fsb = list.FirstOrDefault(s => s.Type == ArgusSensorType.CpuFrequencyFsb);
        if (fsb is null || fsb.Value <= 0)
            return;

        var freqUnit = string.IsNullOrEmpty(fsb.Unit) ? "MHz" : fsb.Unit;

        var multipliers = list.Where(s => s.Type == ArgusSensorType.CpuMultiplier).ToList();
        if (multipliers.Count == 0)
            return;

        var multUnit = multipliers[0].Unit ?? string.Empty;
        var freqs = new List<double>(multipliers.Count);

        foreach (var mult in multipliers)
        {
            var freq = fsb.Value * mult.Value;
            freqs.Add(freq);

            var label = string.IsNullOrWhiteSpace(mult.Label)
                ? $"CPU Core #{mult.SensorIndex} Frequency"
                : $"{mult.Label} Frequency";

            list.Add(new ArgusSensor(
                ArgusSensorType.CpuFrequency,
                label,
                freqUnit,
                freq,
                mult.DataIndex,
                mult.SensorIndex));
        }

        AddAggregate(list, ArgusSensorType.CpuFrequencyMax, "CPU Frequency Max", freqUnit, freqs.Max());
        AddAggregate(list, ArgusSensorType.CpuFrequencyMin, "CPU Frequency Min", freqUnit, freqs.Min());
        AddAggregate(list, ArgusSensorType.CpuFrequencyAvg, "CPU Frequency Avg", freqUnit, freqs.Average());

        AddAggregate(list, ArgusSensorType.CpuMultiplierMax, "CPU Multiplier Max", multUnit, multipliers.Max(m => m.Value));
        AddAggregate(list, ArgusSensorType.CpuMultiplierMin, "CPU Multiplier Min", multUnit, multipliers.Min(m => m.Value));
        AddAggregate(list, ArgusSensorType.CpuMultiplierAvg, "CPU Multiplier Avg", multUnit, multipliers.Average(m => m.Value));
    }

    private static void AddAggregate(List<ArgusSensor> list, ArgusSensorType type, string label, string unit, double value)
    {
        list.Add(new ArgusSensor(type, label, unit, value, 0, 0));
    }

    private static string ReadWideString(ReadOnlySpan<byte> bytes)
    {
        // UTF-16 little-endian, NUL-terminated. Find the first 0x0000 char.
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0)
                return Encoding.Unicode.GetString(bytes.Slice(0, i));
        }
        return Encoding.Unicode.GetString(bytes);
    }
}

/// <summary>A status message of <see cref="ArgusMonitorService"/>: an English format string and its
/// arguments, translated by the plugin when shown.</summary>
public sealed record ArgusDiagnostics(string Format, object[] Args)
{
    public string Text => string.Format(CultureInfo.InvariantCulture, Format, Args);
}
