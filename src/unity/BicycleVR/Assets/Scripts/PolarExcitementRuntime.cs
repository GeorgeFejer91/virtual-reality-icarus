using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class PolarExcitementRuntime : MonoBehaviour
{
    private const string RuntimeName = "Polar Excitement Runtime";
    private const string SavedAddressKey = "PolarH10.DeviceAddress";
    private const string SavedNameKey = "PolarH10.DeviceName";

    public static PolarExcitementRuntime Instance { get; private set; }

    [Header("Startup")]
    public bool autoStartOnAndroid = true;
    public bool autoScanWhenNoSavedDevice = true;
    public int scanDurationMs = 60000;
    public string preferredDeviceAddress = "";

    [Header("Excitement")]
    public float baselineCaptureSeconds = 20.0f;
    public int minimumBaselineSamples = 8;
    public float calmOffsetBpm = 3.0f;
    public float excitedOffsetBpm = 28.0f;
    public float excitementSmoothing = 3.0f;
    public float staleAfterSeconds = 4.0f;

    public int HeartRateBpm { get; private set; }
    public float BaselineBpm { get; private set; }
    public float Excitement01 { get; private set; }
    public bool HasHeartRate { get; private set; }
    public bool IsConnected { get { return polarAdapter != null && polarAdapter.IsConnected; } }
    public string ConnectedDeviceName { get; private set; }
    public string ConnectedDeviceAddress { get; private set; }
    public string Status { get; private set; }

    private BleScanAdapter scanAdapter;
    private PolarPmdAdapter polarAdapter;
    private readonly List<int> baselineSamples = new List<int>();
    private float baselineStartedAt;
    private float lastHeartRateAt = -9999.0f;
    private float targetExcitement;
    private bool baselineLocked;
    private bool startupStarted;
    private bool scanStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureAfterSceneLoad()
    {
        EnsureRuntime();
    }

    public static PolarExcitementRuntime EnsureRuntime()
    {
        if (Instance != null)
            return Instance;

        var existing = FindObjectOfType<PolarExcitementRuntime>();
        if (existing != null)
            return existing;

        var obj = new GameObject(RuntimeName);
        return obj.AddComponent<PolarExcitementRuntime>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Status = "idle";
        EnsureAdapters();
    }

    private void Start()
    {
        if (autoStartOnAndroid)
            Begin();
    }

    private void OnEnable()
    {
        EnsureAdapters();
        HookEvents();
    }

    private void OnDisable()
    {
        UnhookEvents();
    }

    private void Update()
    {
        if (HasHeartRate && Time.time - lastHeartRateAt > staleAfterSeconds)
        {
            HasHeartRate = false;
            targetExcitement = 0.0f;
            Status = "Polar HR stale";
        }

        Excitement01 = Mathf.MoveTowards(
            Excitement01,
            targetExcitement,
            Mathf.Max(0.1f, excitementSmoothing) * Time.deltaTime);
    }

    public void Begin()
    {
        if (startupStarted)
            return;

        startupStarted = true;
        StartCoroutine(BeginRoutine());
    }

    public string BuildStatusLine()
    {
        if (HasHeartRate)
        {
            return string.Format(
                "{0} | {1} bpm | base {2:0} | excite {3:0}%",
                string.IsNullOrEmpty(ConnectedDeviceName) ? "Polar H10" : ConnectedDeviceName,
                HeartRateBpm,
                BaselineBpm,
                Excitement01 * 100.0f);
        }

        return string.IsNullOrEmpty(Status) ? "Polar idle" : Status;
    }

    private IEnumerator BeginRoutine()
    {
        EnsureAdapters();
        Status = "requesting BLE permissions";
        RequestBlePermissions();
        yield return new WaitForSeconds(1.0f);

        Status = "initializing Polar bridge";
        polarAdapter.Initialize();
        scanAdapter.Initialize();

        yield return new WaitForSeconds(0.5f);

        string address = preferredDeviceAddress;
        if (string.IsNullOrEmpty(address))
            address = PlayerPrefs.GetString(SavedAddressKey, "");

        if (!string.IsNullOrEmpty(address))
        {
            ConnectedDeviceAddress = address;
            ConnectedDeviceName = PlayerPrefs.GetString(SavedNameKey, "Polar H10");
            Status = "connecting to saved Polar H10";
            polarAdapter.ConnectAndSubscribe(address);
            yield break;
        }

        if (autoScanWhenNoSavedDevice)
            StartScan();
        else
            Status = "no saved Polar H10";
    }

    private void StartScan()
    {
        if (scanStarted)
            return;

        scanStarted = true;
        Status = "scanning for Polar H10";
        scanAdapter.StartScan(scanDurationMs);
    }

    private void EnsureAdapters()
    {
        polarAdapter = GetComponent<PolarPmdAdapter>();
        if (polarAdapter == null)
            polarAdapter = gameObject.AddComponent<PolarPmdAdapter>();

        scanAdapter = FindObjectOfType<BleScanAdapter>();
        if (scanAdapter == null)
        {
            var scanner = new GameObject("BleAdapter");
            DontDestroyOnLoad(scanner);
            scanAdapter = scanner.AddComponent<BleScanAdapter>();
        }
    }

    private void HookEvents()
    {
        if (scanAdapter != null)
        {
            scanAdapter.DeviceDiscovered += HandleDeviceDiscovered;
            scanAdapter.ScanFinished += HandleScanFinished;
            scanAdapter.ErrorReceived += HandleScanError;
        }

        if (polarAdapter != null)
        {
            polarAdapter.Connected += HandlePolarConnected;
            polarAdapter.Disconnected += HandlePolarDisconnected;
            polarAdapter.HeartRateDataReceived += HandleHeartRateData;
            polarAdapter.ErrorReceived += HandlePolarError;
        }
    }

    private void UnhookEvents()
    {
        if (scanAdapter != null)
        {
            scanAdapter.DeviceDiscovered -= HandleDeviceDiscovered;
            scanAdapter.ScanFinished -= HandleScanFinished;
            scanAdapter.ErrorReceived -= HandleScanError;
        }

        if (polarAdapter != null)
        {
            polarAdapter.Connected -= HandlePolarConnected;
            polarAdapter.Disconnected -= HandlePolarDisconnected;
            polarAdapter.HeartRateDataReceived -= HandleHeartRateData;
            polarAdapter.ErrorReceived -= HandlePolarError;
        }
    }

    private void HandleDeviceDiscovered(string address, string name)
    {
        if (string.IsNullOrEmpty(address))
            return;

        string safeName = string.IsNullOrEmpty(name) ? "unnamed" : name;
        Debug.Log("[PolarExcitement] BLE device: " + safeName + " @ " + address);

        if (!IsPolarCandidate(safeName))
            return;

        scanAdapter.StopScan();
        scanStarted = false;
        ConnectedDeviceAddress = address;
        ConnectedDeviceName = safeName;
        PlayerPrefs.SetString(SavedAddressKey, address);
        PlayerPrefs.SetString(SavedNameKey, safeName);
        PlayerPrefs.Save();
        Status = "connecting to " + safeName;
        polarAdapter.ConnectAndSubscribe(address);
    }

    private static bool IsPolarCandidate(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        return name.IndexOf("Polar", StringComparison.OrdinalIgnoreCase) >= 0 &&
               (name.IndexOf("H10", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("H9", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void HandleScanFinished()
    {
        scanStarted = false;
        if (!IsConnected)
            Status = "scan finished; no Polar H10 found";
    }

    private void HandleScanError(string message)
    {
        Status = "BLE scan error: " + message;
    }

    private void HandlePolarConnected(string address)
    {
        ConnectedDeviceAddress = address;
        Status = "connected; waiting for HR";
    }

    private void HandlePolarDisconnected(string address)
    {
        HasHeartRate = false;
        targetExcitement = 0.0f;
        Status = "Polar disconnected";
    }

    private void HandlePolarError(string message)
    {
        Status = "Polar error: " + message;
    }

    private void HandleHeartRateData(byte[] data)
    {
        int hr = DecodeHeartRate(data);
        if (hr <= 0)
            return;

        HeartRateBpm = hr;
        HasHeartRate = true;
        lastHeartRateAt = Time.time;

        if (baselineSamples.Count == 0)
            baselineStartedAt = Time.time;

        if (!baselineLocked)
        {
            baselineSamples.Add(hr);
            BaselineBpm = AverageBaseline();

            float elapsed = Time.time - baselineStartedAt;
            if (baselineSamples.Count >= minimumBaselineSamples && elapsed >= baselineCaptureSeconds)
                baselineLocked = true;
        }

        float low = BaselineBpm + calmOffsetBpm;
        float high = BaselineBpm + Mathf.Max(calmOffsetBpm + 1.0f, excitedOffsetBpm);
        targetExcitement = Mathf.Clamp01((hr - low) / Mathf.Max(1.0f, high - low));
        Status = "receiving HR";
    }

    private float AverageBaseline()
    {
        if (baselineSamples.Count == 0)
            return 0.0f;

        int sum = 0;
        for (int i = 0; i < baselineSamples.Count; i++)
            sum += baselineSamples[i];

        return (float)sum / baselineSamples.Count;
    }

    private static int DecodeHeartRate(byte[] data)
    {
        if (data == null || data.Length < 2)
            return 0;

        int flags = data[0] & 0xFF;
        bool is16Bit = (flags & 0x01) != 0;

        if (is16Bit && data.Length >= 3)
            return (data[1] & 0xFF) | ((data[2] & 0xFF) << 8);

        return data[1] & 0xFF;
    }

    private static void RequestBlePermissions()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        int sdkInt = GetAndroidSdkInt();
        var desired = new List<string>();

        if (sdkInt >= 31)
        {
            desired.Add("android.permission.BLUETOOTH_SCAN");
            desired.Add("android.permission.BLUETOOTH_CONNECT");
        }
        else
        {
            desired.Add("android.permission.ACCESS_FINE_LOCATION");
            desired.Add("android.permission.BLUETOOTH");
            desired.Add("android.permission.BLUETOOTH_ADMIN");
        }

        using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
        {
            if (activity == null)
                return;

            if (sdkInt >= 23)
            {
                var missing = new List<string>();
                for (int i = 0; i < desired.Count; i++)
                {
                    int result = activity.Call<int>("checkSelfPermission", desired[i]);
                    if (result != 0)
                        missing.Add(desired[i]);
                }

                if (missing.Count > 0)
                    activity.Call("requestPermissions", missing.ToArray(), 7042);
            }
        }
#endif
    }

    private static int GetAndroidSdkInt()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            return version.GetStatic<int>("SDK_INT");
#else
        return 0;
#endif
    }
}
