using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinCare.Native;

public static class CommandLine
{
    private const int MaximumWindowsCommandLineCharacters = 32_767;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static string[] Split(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<string>();
        }

        if (commandLine.Length > MaximumWindowsCommandLineCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(commandLine),
                $"Windows command lines cannot exceed {MaximumWindowsCommandLineCharacters} characters.");
        }

        IntPtr arguments = CommandLineToArgvW(commandLine, out int count);
        if (arguments == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            if (count < 0 || count > 32_768)
            {
                throw new InvalidDataException($"CommandLineToArgvW returned an invalid argument count: {count}.");
            }

            var result = new string[count];
            for (int index = 0; index < count; index++)
            {
                IntPtr value = Marshal.ReadIntPtr(arguments, checked(index * IntPtr.Size));
                result[index] = Marshal.PtrToStringUni(value)
                    ?? throw new InvalidDataException($"CommandLineToArgvW returned a null argument at index {index}.");
            }

            return result;
        }
        finally
        {
            _ = LocalFree(arguments);
        }
    }
}
