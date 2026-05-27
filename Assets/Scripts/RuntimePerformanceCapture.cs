using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

public class RuntimePerformanceCapture : MonoBehaviour
{
    private const int MaxSpikeEntries = 25;
    private const float DefaultSpikeFrameMs = 25f;

    private static RuntimePerformanceCapture _instance;

    [SerializeField] private float spikeFrameThresholdMs = DefaultSpikeFrameMs;

    private readonly List<FrameSpikeSample> _spikes = new List<FrameSpikeSample>();
    private float _sessionStartRealtime;
    private float _accumulatedFrameMs;
    private float _maxFrameMs;
    private float _minFrameMs = float.MaxValue;
    private int _frameCount;
    private long _startAllocatedMemory;
    private long _startReservedMemory;
    private long _startMonoMemory;
    private long _maxAllocatedMemory;
    private long _maxReservedMemory;
    private long _maxMonoMemory;
    private int _gc0Start;
    private int _gc1Start;
    private int _gc2Start;
    private bool _hasWrittenReport;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateSingleton()
    {
        if (_instance != null)
        {
            return;
        }

        GameObject runtimeObject = new GameObject("RuntimePerformanceCapture");
        DontDestroyOnLoad(runtimeObject);
        _instance = runtimeObject.AddComponent<RuntimePerformanceCapture>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        _sessionStartRealtime = Time.realtimeSinceStartup;
        _startAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong();
        _startReservedMemory = Profiler.GetTotalReservedMemoryLong();
        _startMonoMemory = Profiler.GetMonoUsedSizeLong();
        _maxAllocatedMemory = _startAllocatedMemory;
        _maxReservedMemory = _startReservedMemory;
        _maxMonoMemory = _startMonoMemory;
        _gc0Start = GC.CollectionCount(0);
        _gc1Start = GC.CollectionCount(1);
        _gc2Start = GC.CollectionCount(2);
    }

    private void Update()
    {
        float frameMs = Time.unscaledDeltaTime * 1000f;
        _frameCount++;
        _accumulatedFrameMs += frameMs;
        _maxFrameMs = Mathf.Max(_maxFrameMs, frameMs);
        _minFrameMs = Mathf.Min(_minFrameMs, frameMs);

        long allocatedMemory = Profiler.GetTotalAllocatedMemoryLong();
        long reservedMemory = Profiler.GetTotalReservedMemoryLong();
        long monoMemory = Profiler.GetMonoUsedSizeLong();
        _maxAllocatedMemory = Math.Max(_maxAllocatedMemory, allocatedMemory);
        _maxReservedMemory = Math.Max(_maxReservedMemory, reservedMemory);
        _maxMonoMemory = Math.Max(_maxMonoMemory, monoMemory);

        if (frameMs >= spikeFrameThresholdMs)
        {
            RecordSpike(frameMs, allocatedMemory, reservedMemory, monoMemory);
        }
    }

    private void OnApplicationQuit()
    {
        WriteReport();
    }

    private void OnDestroy()
    {
        WriteReport();
    }

    private void RecordSpike(float frameMs, long allocatedMemory, long reservedMemory, long monoMemory)
    {
        FrameSpikeSample sample = new FrameSpikeSample
        {
            timeSinceStart = Time.realtimeSinceStartup - _sessionStartRealtime,
            frameMs = frameMs,
            allocatedMemory = allocatedMemory,
            reservedMemory = reservedMemory,
            monoMemory = monoMemory,
            unitCount = TeamManager.GetTotalRegisteredUnitCount(),
            buildCount = TeamManager.GetTotalRegisteredBuildCount(),
            treeCount = Tree.ActiveCount
        };

        _spikes.Add(sample);
        _spikes.Sort((left, right) => right.frameMs.CompareTo(left.frameMs));
        if (_spikes.Count > MaxSpikeEntries)
        {
            _spikes.RemoveAt(_spikes.Count - 1);
        }
    }

    private void WriteReport()
    {
        if (_hasWrittenReport)
        {
            return;
        }

        _hasWrittenReport = true;

        try
        {
            string reportDirectory = ResolveReportDirectory();
            Directory.CreateDirectory(reportDirectory);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string reportPath = Path.Combine(reportDirectory, $"perf-report-{timestamp}.txt");
            File.WriteAllText(reportPath, BuildReportText());
            Debug.Log($"Performance report written to: {reportPath}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Failed to write performance report: {exception.Message}");
        }
    }

    private string BuildReportText()
    {
        float sessionDuration = Mathf.Max(0.001f, Time.realtimeSinceStartup - _sessionStartRealtime);
        float averageFrameMs = _frameCount > 0 ? _accumulatedFrameMs / _frameCount : 0f;
        float averageFps = averageFrameMs > 0.001f ? 1000f / averageFrameMs : 0f;
        float minFps = _maxFrameMs > 0.001f ? 1000f / _maxFrameMs : 0f;
        float maxFps = _minFrameMs > 0.001f && _minFrameMs < float.MaxValue ? 1000f / _minFrameMs : 0f;

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("RTSLords Runtime Performance Report");
        builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Session duration: {sessionDuration:F2}s");
        builder.AppendLine($"Frames captured: {_frameCount}");
        builder.AppendLine();
        builder.AppendLine("Frame timing");
        builder.AppendLine($"Average frame: {averageFrameMs:F2} ms ({averageFps:F1} FPS)");
        builder.AppendLine($"Worst frame: {_maxFrameMs:F2} ms ({minFps:F1} FPS)");
        builder.AppendLine($"Best frame: {(_minFrameMs == float.MaxValue ? 0f : _minFrameMs):F2} ms ({maxFps:F1} FPS)");
        builder.AppendLine($"Spike threshold: {spikeFrameThresholdMs:F2} ms");
        builder.AppendLine();
        builder.AppendLine("Memory");
        builder.AppendLine($"Allocated start/end/max: {FormatBytes(_startAllocatedMemory)} / {FormatBytes(Profiler.GetTotalAllocatedMemoryLong())} / {FormatBytes(_maxAllocatedMemory)}");
        builder.AppendLine($"Reserved start/end/max: {FormatBytes(_startReservedMemory)} / {FormatBytes(Profiler.GetTotalReservedMemoryLong())} / {FormatBytes(_maxReservedMemory)}");
        builder.AppendLine($"Mono start/end/max: {FormatBytes(_startMonoMemory)} / {FormatBytes(Profiler.GetMonoUsedSizeLong())} / {FormatBytes(_maxMonoMemory)}");
        builder.AppendLine();
        builder.AppendLine("GC collections");
        builder.AppendLine($"Gen0: {GC.CollectionCount(0) - _gc0Start}");
        builder.AppendLine($"Gen1: {GC.CollectionCount(1) - _gc1Start}");
        builder.AppendLine($"Gen2: {GC.CollectionCount(2) - _gc2Start}");
        builder.AppendLine();
        builder.AppendLine("Final world counts");
        builder.AppendLine($"Units: {TeamManager.GetTotalRegisteredUnitCount()}");
        builder.AppendLine($"Builds: {TeamManager.GetTotalRegisteredBuildCount()}");
        builder.AppendLine($"Trees: {Tree.ActiveCount}");
        builder.AppendLine($"Teams: {TeamManager.ActiveTeamCount}");
        builder.AppendLine();
        builder.AppendLine("Worst spikes");

        if (_spikes.Count == 0)
        {
            builder.AppendLine("No frame spike above threshold recorded.");
        }
        else
        {
            for (int i = 0; i < _spikes.Count; i++)
            {
                FrameSpikeSample sample = _spikes[i];
                builder.AppendLine(
                    $"{i + 1}. t={sample.timeSinceStart:F2}s | {sample.frameMs:F2} ms | " +
                    $"alloc={FormatBytes(sample.allocatedMemory)} | reserved={FormatBytes(sample.reservedMemory)} | mono={FormatBytes(sample.monoMemory)} | " +
                    $"units={sample.unitCount} | builds={sample.buildCount} | trees={sample.treeCount}");
            }
        }

        return builder.ToString();
    }

    private string ResolveReportDirectory()
    {
#if UNITY_EDITOR
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, "PerfReports");
#else
        return Path.Combine(Application.persistentDataPath, "PerfReports");
#endif
    }

    private static string FormatBytes(long bytes)
    {
        const float megabyte = 1024f * 1024f;
        return $"{bytes / megabyte:F2} MB";
    }

    private struct FrameSpikeSample
    {
        public float timeSinceStart;
        public float frameMs;
        public long allocatedMemory;
        public long reservedMemory;
        public long monoMemory;
        public int unitCount;
        public int buildCount;
        public int treeCount;
    }
}
