using System;
using UnityEngine;

public sealed class BleScanAdapter : MonoBehaviour
{
    public event Action<string, string> DeviceDiscovered;
    public event Action ScanFinished;
    public event Action<string> ErrorReceived;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject bleLibrary;
#endif

    private void Awake()
    {
        gameObject.name = "BleAdapter";
    }

    public void Initialize()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var cls = new AndroidJavaClass("com.velorexe.unityandroidble.UnityAndroidBLE"))
            {
                bleLibrary = cls.CallStatic<AndroidJavaObject>("getInstance");
            }
        }
        catch (Exception ex)
        {
            RaiseError("BLE scan init failed: " + ex.Message);
        }
#endif
    }

    public void StartScan(int scanDurationMs)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (bleLibrary == null)
            Initialize();

        if (bleLibrary == null)
        {
            RaiseError("BLE scanner unavailable");
            return;
        }

        bleLibrary.Call("scanBleDevices", Mathf.Max(1000, scanDurationMs));
#else
        Debug.Log("[BleScanAdapter] StartScan ignored outside Android device build.");
#endif
    }

    public void StopScan()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (bleLibrary != null)
            bleLibrary.Call("stopScanBleDevices");
#endif
    }

    public void OnBleMessage(string jsonMessage)
    {
        if (string.IsNullOrEmpty(jsonMessage))
        {
            RaiseError("BLE plugin sent an empty message");
            return;
        }

        BleMessage msg;
        try
        {
            msg = JsonUtility.FromJson<BleMessage>(jsonMessage);
        }
        catch (Exception ex)
        {
            RaiseError("BLE message parse failed: " + ex.Message);
            return;
        }

        if (msg == null)
        {
            RaiseError("BLE message parsed to null");
            return;
        }

        if (msg.hasError)
        {
            RaiseError(string.IsNullOrEmpty(msg.errorMessage) ? "BLE plugin error" : msg.errorMessage);
            return;
        }

        if (string.Equals(msg.command, "DiscoveredDevice", StringComparison.OrdinalIgnoreCase))
        {
            if (DeviceDiscovered != null)
                DeviceDiscovered(msg.device, msg.name);
            return;
        }

        if (string.Equals(msg.command, "FinishedDiscovering", StringComparison.OrdinalIgnoreCase))
        {
            if (ScanFinished != null)
                ScanFinished();
        }
    }

    public void LogMessage(string log)
    {
        Debug.Log("[BleScanAdapter] " + log);
    }

    private void RaiseError(string message)
    {
        Debug.LogWarning("[BleScanAdapter] " + message);
        if (ErrorReceived != null)
            ErrorReceived(message);
    }

    [Serializable]
    private sealed class BleMessage
    {
        public string command;
        public string device;
        public string name;
        public bool hasError;
        public string errorMessage;
    }
}
