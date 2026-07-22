using System;
using System.Runtime.InteropServices;
namespace WinCare.Native {
    enum EDataFlow { eRender, eCapture, eAll }
    enum ERole { eConsole, eMultimedia, eCommunications }
    [Flags] enum CLSCTX : uint { InprocServer=1, InprocHandler=2, LocalServer=4, RemoteServer=16, All=23 }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    interface IMMDeviceEnumerator {
        int NotImpl1();
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid, CLSCTX clsctx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    interface IAudioEndpointVolume {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float levelDb, Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, Guid eventContext);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDb, Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid eventContext);
        int GetMute(out bool mute);
    }
    public static class AudioEndpoint {
        static IAudioEndpointVolume Endpoint() {
            var enumerator=(IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice device; Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender,ERole.eMultimedia,out device));
            var iid=typeof(IAudioEndpointVolume).GUID; object endpoint;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid,CLSCTX.All,IntPtr.Zero,out endpoint));
            return (IAudioEndpointVolume)endpoint;
        }
        public static float GetVolume() { float value; Marshal.ThrowExceptionForHR(Endpoint().GetMasterVolumeLevelScalar(out value)); return value; }
        public static bool GetMute() { bool value; Marshal.ThrowExceptionForHR(Endpoint().GetMute(out value)); return value; }
        public static void SetVolume(float value) { if(value<0 || value>1) throw new ArgumentOutOfRangeException("value"); Marshal.ThrowExceptionForHR(Endpoint().SetMasterVolumeLevelScalar(value,Guid.Empty)); }
        public static void SetMute(bool value) { Marshal.ThrowExceptionForHR(Endpoint().SetMute(value,Guid.Empty)); }
    }
}



