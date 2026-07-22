using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Microsoft.Win32;

namespace WinCare.Native
{
    /// <summary>
    /// Self-Inclusive Native Policy Engine for Reading/Writing Binary Registry Policy (.pol) Files
    /// Derived from PolicyPlus (Project 054) - Zero Third-Party Binary Dependencies.
    /// </summary>
    public static class PolicyEngine
    {
        private static readonly byte[] PRegHeader = new byte[] { 0x50, 0x52, 0x65, 0x67, 0x01, 0x00, 0x00, 0x00 }; // "PReg" Magic Header + Version 1

        public class PolEntry
        {
            public string KeyName { get; set; }
            public string ValueName { get; set; }
            public RegistryValueKind Type { get; set; }
            public byte[] Data { get; set; }
        }

        public static System.Collections.Generic.List<PolEntry> LoadPolFile(string filePath)
        {
            var entries = new System.Collections.Generic.List<PolEntry>();
            if (!File.Exists(filePath)) return entries;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs, Encoding.Unicode))
            {
                byte[] header = br.ReadBytes(8);
                if (header.Length < 8 || !HeaderMatches(header))
                {
                    throw new InvalidDataException("Invalid PReg magic header in policy file.");
                }

                while (fs.Position < fs.Length)
                {
                    char openBracket = br.ReadChar();
                    if (openBracket != '[') break;

                    string key = ReadNullTerminatedString(br);
                    char sep1 = br.ReadChar(); // ';'
                    string value = ReadNullTerminatedString(br);
                    char sep2 = br.ReadChar(); // ';'
                    uint typeVal = br.ReadUInt32();
                    char sep3 = br.ReadChar(); // ';'
                    uint size = br.ReadUInt32();
                    char sep4 = br.ReadChar(); // ';'
                    byte[] data = br.ReadBytes((int)size);
                    char closeBracket = br.ReadChar(); // ']'

                    entries.Add(new PolEntry
                    {
                        KeyName = key,
                        ValueName = value,
                        Type = (RegistryValueKind)typeVal,
                        Data = data
                    });
                }
            }
            return entries;
        }

        public static void SavePolFile(string filePath, System.Collections.Generic.List<PolEntry> entries)
        {
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs, Encoding.Unicode))
            {
                bw.Write(PRegHeader);
                foreach (var entry in entries)
                {
                    bw.Write('[');
                    WriteNullTerminatedString(bw, entry.KeyName);
                    bw.Write(';');
                    WriteNullTerminatedString(bw, entry.ValueName);
                    bw.Write(';');
                    bw.Write((uint)entry.Type);
                    bw.Write(';');
                    bw.Write((uint)entry.Data.Length);
                    bw.Write(';');
                    bw.Write(entry.Data);
                    bw.Write(']');
                }
            }
        }

        private static bool HeaderMatches(byte[] header)
        {
            for (int i = 0; i < 8; i++)
            {
                if (header[i] != PRegHeader[i]) return false;
            }
            return true;
        }

        private static string ReadNullTerminatedString(BinaryReader br)
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                char c = br.ReadChar();
                if (c == '\0') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static void WriteNullTerminatedString(BinaryWriter bw, string str)
        {
            if (str != null)
            {
                foreach (char c in str) bw.Write(c);
            }
            bw.Write('\0');
        }
    }
}
