using System;
using UnityEngine;

public sealed class PolarPmdAdapter : MonoBehaviour
{
    public event Action<bool> Initialized;
    public event Action<string> Connected;
    public event Action<string> Disconnected;
    public event Action<byte[]> HeartRateDataReceived;
    public event Action<byte[]> PmdControlDataReceived;
    public event Action<byte[]> PmdStreamDataReceived;
    public event Action<string> ErrorReceived;

    public bool IsInitialized { get; private set; }
    public bool IsConnected { get; private set; }
    public bool IsPmdReady { get; private set; }
    public int HeartRateNotificationCount { get; private set; }
    public int PmdStreamNotificationCount { get; private set; }
    public string LastError { get; private set; }

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaClass bridgeClass;
#endif

    private void Awake()
    {
        gameObject.name = "PolarPmdAdapter";
    }

    public void Initialize()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            bridgeClass = new AndroidJavaClass("com.viscereality.polarpmd.PolarPmdBridge");
            bridgeClass.CallStatic("staticInitialize");
        }
        catch (Exception ex)
        {
            RaiseError("Polar bridge init failed: " + ex.Message);
        }
#else
        IsInitialized = true;
        if (Initialized != null)
            Initialized(true);
#endif
    }

    public void ConnectAndSubscribe(string deviceAddress)
    {
        if (string.IsNullOrEmpty(deviceAddress))
        {
            RaiseError("Cannot connect to an empty Polar address");
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (bridgeClass == null)
            Initialize();

        if (bridgeClass == null)
        {
            RaiseError("Polar bridge unavailable");
            return;
        }

        bridgeClass.CallStatic("staticConnectAndSubscribePmd", deviceAddress);
#else
        Debug.Log("[PolarPmdAdapter] Connect ignored outside Android device build: " + deviceAddress);
#endif
    }

    public void Disconnect()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (bridgeClass != null)
            bridgeClass.CallStatic("staticDisconnect");
#endif
        IsConnected = false;
        IsPmdReady = false;
    }

    public void WritePmdCommand(byte[] data)
    {
        if (data == null || data.Length == 0)
            return;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (bridgeClass == null)
            return;

        bridgeClass.CallStatic("staticWritePmdCommand", Convert.ToBase64String(data));
#endif
    }

    public void OnPmdMessage(string jsonMessage)
    {
        if (string.IsNullOrEmpty(jsonMessage))
        {
            RaiseError("Polar bridge sent an empty message");
            return;
        }

        PmdMessage msg;
        try
        {
            msg = JsonUtility.FromJson<PmdMessage>(jsonMessage);
        }
        catch (Exception ex)
        {
            RaiseError("Polar bridge JSON parse failed: " + ex.Message);
            return;
        }

        if (msg == null)
        {
            RaiseError("Polar bridge message parsed to null");
            return;
        }

        if (msg.command == "Initialized")
        {
            IsInitialized = msg.data == "true";
            if (Initialized != null)
                Initialized(IsInitialized);
            return;
        }

        if (msg.command == "Connected")
        {
            IsConnected = true;
            if (Connected != null)
                Connected(msg.data);
            return;
        }

        if (msg.command == "Disconnected")
        {
            IsConnected = false;
            IsPmdReady = false;
            if (Disconnected != null)
                Disconnected(msg.data);
            return;
        }

        if (msg.command == "PmdReady")
        {
            IsPmdReady = true;
            return;
        }

        if (msg.command == "HrData")
        {
            HeartRateNotificationCount++;
            RaiseBytes(msg.data, HeartRateDataReceived);
            return;
        }

        if (msg.command == "PmdCtrlData")
        {
            RaiseBytes(msg.data, PmdControlDataReceived);
            return;
        }

        if (msg.command == "PmdDataData")
        {
            PmdStreamNotificationCount++;
            RaiseBytes(msg.data, PmdStreamDataReceived);
            return;
        }

        if (msg.command == "Error")
            RaiseError(msg.data);
    }

    private void RaiseBytes(string base64, Action<byte[]> handler)
    {
        if (handler == null || string.IsNullOrEmpty(base64))
            return;

        try
        {
            handler(Convert.FromBase64String(base64));
        }
        catch (Exception ex)
        {
            RaiseError("Polar payload decode failed: " + ex.Message);
        }
    }

    private void RaiseError(string message)
    {
        LastError = message;
        Debug.LogWarning("[PolarPmdAdapter] " + message);
        if (ErrorReceived != null)
            ErrorReceived(message);
    }

    [Serializable]
    private sealed class PmdMessage
    {
        public string command;
        public string data;
    }
}
