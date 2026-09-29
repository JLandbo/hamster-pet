using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Hamster.Desktop;

public sealed partial class SystemVolume
{
    const uint _allContexts = 0x17;
    const int _render = 0;
    const int _multimedia = 1;
    static readonly Guid _deviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    public (double Level, bool Muted)? Read()
    {
        try
        {
            var endpoint = Endpoint();
            return (endpoint.GetMasterVolumeLevelScalar(), endpoint.GetMute());
        }
        catch (COMException)
        {
            return null;
        }
    }

    public void SetLevel(double level) => Change(endpoint => endpoint.SetMasterVolumeLevelScalar((float)level, Guid.Empty));

    public void SetMuted(bool muted) => Change(endpoint => endpoint.SetMute(muted, Guid.Empty));

    static void Change(Action<IAudioEndpointVolume> change)
    {
        try
        {
            change(Endpoint());
        }
        catch (COMException)
        {
        }
    }

    static IAudioEndpointVolume Endpoint()
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(_deviceEnumerator, 0, _allContexts, typeof(IMMDeviceEnumerator).GUID, out var devices));
        return devices.GetDefaultAudioEndpoint(_render, _multimedia).Activate(typeof(IAudioEndpointVolume).GUID, _allContexts, 0);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid interfaceId, out IMMDeviceEnumerator instance);
}

[GeneratedComInterface]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    nint EnumAudioEndpoints(int dataFlow, uint stateMask);
    IMMDevice GetDefaultAudioEndpoint(int dataFlow, int role);
}

[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    IAudioEndpointVolume Activate(in Guid interfaceId, uint context, nint activationParameters);
}

[GeneratedComInterface]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal partial interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(nint notify);
    void UnregisterControlChangeNotify(nint notify);
    uint GetChannelCount();
    void SetMasterVolumeLevel(float decibels, in Guid context);
    void SetMasterVolumeLevelScalar(float level, in Guid context);
    float GetMasterVolumeLevel();
    float GetMasterVolumeLevelScalar();
    void SetChannelVolumeLevel(uint channel, float decibels, in Guid context);
    void SetChannelVolumeLevelScalar(uint channel, float level, in Guid context);
    float GetChannelVolumeLevel(uint channel);
    float GetChannelVolumeLevelScalar(uint channel);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, in Guid context);
    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetMute();
}
