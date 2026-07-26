using System;
using System.Runtime.InteropServices;

namespace WinCare.Native
{
    internal enum EDataFlow { Render, Capture, All }
    internal enum ERole { Console, Multimedia, Communications }

    [Flags]
    internal enum ClsContext : uint
    {
        InprocServer = 1,
        InprocHandler = 2,
        LocalServer = 4,
        RemoteServer = 16,
        All = 23
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal sealed class MmDeviceEnumerator { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    internal interface IMmDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMmDevice device);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    internal interface IMmDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, ClsContext context, IntPtr activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    public static class AudioEndpoint
    {
        private static T WithEndpoint<T>(Func<IAudioEndpointVolume, T> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            object? enumeratorObject = null;
            object? deviceObject = null;
            object? endpointObject = null;
            try
            {
                enumeratorObject = new MmDeviceEnumerator();
                var enumerator = (IMmDeviceEnumerator)enumeratorObject;
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out IMmDevice device));
                deviceObject = device;
                Guid interfaceId = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref interfaceId, ClsContext.All, IntPtr.Zero, out endpointObject));
                return action((IAudioEndpointVolume)endpointObject);
            }
            finally
            {
                ReleaseComObject(endpointObject);
                ReleaseComObject(deviceObject);
                ReleaseComObject(enumeratorObject);
            }
        }

        private static void ReleaseComObject(object? value)
        {
            if (value is not null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }

        public static float GetVolume() => WithEndpoint(endpoint =>
        {
            Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out float value));
            return value;
        });

        public static bool GetMute() => WithEndpoint(endpoint =>
        {
            Marshal.ThrowExceptionForHR(endpoint.GetMute(out bool value));
            return value;
        });

        public static void SetVolume(float value)
        {
            if (!float.IsFinite(value) || value is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Volume must be finite and between 0 and 1.");
            _ = WithEndpoint(endpoint =>
            {
                Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(value, Guid.Empty));
                return true;
            });
        }

        public static void SetMute(bool value)
        {
            _ = WithEndpoint(endpoint =>
            {
                Marshal.ThrowExceptionForHR(endpoint.SetMute(value, Guid.Empty));
                return true;
            });
        }
    }
}
