using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace WinCare.Native
{
    /// <summary>
    /// Bounded reader and atomic writer for Registry.pol files.
    /// </summary>
    public static class PolicyEngine
    {
        private static readonly byte[] PRegHeader = { 0x50, 0x52, 0x65, 0x67, 0x01, 0x00, 0x00, 0x00 };
        private const long MaximumFileBytes = 64L * 1024 * 1024;
        private const int MaximumEntries = 100_000;
        private const int MaximumStringCharacters = 32_768;
        private const int MaximumEntryDataBytes = 16 * 1024 * 1024;
        private const long MaximumTotalDataBytes = 64L * 1024 * 1024;

        public sealed class PolEntry
        {
            public string KeyName { get; set; } = string.Empty;
            public string ValueName { get; set; } = string.Empty;
            public RegistryValueKind Type { get; set; }
            public byte[] Data { get; set; } = Array.Empty<byte>();
        }

        public static List<PolEntry> LoadPolFile(string filePath)
        {
            string path = ValidatePath(filePath);
            if (!File.Exists(path)) return new List<PolEntry>();
            FileInfo before = GetRegularFile(path);
            if (before.Length < PRegHeader.Length || before.Length > MaximumFileBytes)
                throw new InvalidDataException($"Registry policy size is outside {PRegHeader.Length}..{MaximumFileBytes} bytes.");

            var entries = new List<PolEntry>();
            long totalDataBytes = 0;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan))
            using (var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: false))
            {
                if (stream.Length != before.Length)
                    throw new IOException("Registry policy changed before it could be read.");
                byte[] header = reader.ReadBytes(PRegHeader.Length);
                if (header.Length != PRegHeader.Length || !HeaderMatches(header))
                    throw new InvalidDataException("Invalid PReg header in registry policy file.");

                while (stream.Position < stream.Length)
                {
                    if (entries.Count >= MaximumEntries)
                        throw new InvalidDataException($"Registry policy contains more than {MaximumEntries} entries.");
                    RequireCharacter(reader, '[', "entry opening bracket");
                    string key = ReadNullTerminatedString(reader, "key name");
                    RequireCharacter(reader, ';', "key separator");
                    string value = ReadNullTerminatedString(reader, "value name");
                    RequireCharacter(reader, ';', "value separator");
                    uint rawType = reader.ReadUInt32();
                    RequireCharacter(reader, ';', "type separator");
                    uint rawSize = reader.ReadUInt32();
                    RequireCharacter(reader, ';', "data separator");

                    if (rawSize > MaximumEntryDataBytes)
                        throw new InvalidDataException($"Registry policy entry data exceeds {MaximumEntryDataBytes} bytes.");
                    if ((long)rawSize > stream.Length - stream.Position - sizeof(char))
                        throw new EndOfStreamException("Registry policy entry data extends beyond the file boundary.");
                    byte[] data = reader.ReadBytes(checked((int)rawSize));
                    if ((uint)data.Length != rawSize)
                        throw new EndOfStreamException("Registry policy entry data was truncated.");
                    RequireCharacter(reader, ']', "entry closing bracket");

                    totalDataBytes = checked(totalDataBytes + data.Length);
                    if (totalDataBytes > MaximumTotalDataBytes)
                        throw new InvalidDataException($"Registry policy data exceeds {MaximumTotalDataBytes} bytes in aggregate.");
                    var type = (RegistryValueKind)unchecked((int)rawType);
                    if (!Enum.IsDefined(typeof(RegistryValueKind), type))
                        throw new InvalidDataException($"Unsupported registry value type: {rawType}.");
                    entries.Add(new PolEntry { KeyName = key, ValueName = value, Type = type, Data = data });
                }
            }

            FileInfo after = GetRegularFile(path);
            if (after.Length != before.Length || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
                throw new IOException("Registry policy changed while it was read.");
            return entries;
        }

        public static void SavePolFile(string filePath, List<PolEntry> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);
            if (entries.Count > MaximumEntries)
                throw new ArgumentOutOfRangeException(nameof(entries), $"At most {MaximumEntries} entries are permitted.");

            string path = ValidatePath(filePath);
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                throw new DirectoryNotFoundException("The registry policy destination directory does not exist.");
            EnsureNoReparseAncestors(directory);
            if (File.Exists(path)) _ = GetRegularFile(path);
            long expectedLength = ValidateEntries(entries);

            string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            string backup = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.bak");
            string failedReplacement = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.failed");
            bool replacedExisting = false;
            bool createdNew = false;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
                using (var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true))
                {
                    writer.Write(PRegHeader);
                    foreach (PolEntry entry in entries)
                    {
                        writer.Write('[');
                        WriteNullTerminatedString(writer, entry.KeyName);
                        writer.Write(';');
                        WriteNullTerminatedString(writer, entry.ValueName);
                        writer.Write(';');
                        writer.Write(unchecked((uint)(int)entry.Type));
                        writer.Write(';');
                        writer.Write((uint)entry.Data.Length);
                        writer.Write(';');
                        writer.Write(entry.Data);
                        writer.Write(']');
                    }
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                    if (stream.Length != expectedLength || stream.Length > MaximumFileBytes)
                        throw new InvalidDataException("Serialized registry policy length does not match its validated model.");
                }

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, backup, ignoreMetadataErrors: false);
                    replacedExisting = true;
                }
                else
                {
                    File.Move(temporary, path);
                    createdNew = true;
                }
                FileInfo written = GetRegularFile(path);
                if (written.Length != expectedLength)
                    throw new IOException("The persisted registry policy length could not be verified.");
                TryDelete(backup);
            }
            catch (Exception writeError)
            {
                Exception? recoveryError = null;
                try
                {
                    if (replacedExisting && File.Exists(backup))
                    {
                        File.Replace(backup, path, failedReplacement, ignoreMetadataErrors: false);
                        TryDelete(failedReplacement);
                    }
                    else if (createdNew && File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    else if (!File.Exists(path) && File.Exists(backup))
                    {
                        File.Move(backup, path);
                    }
                }
                catch (Exception error)
                {
                    recoveryError = error;
                }
                if (recoveryError is not null)
                    throw new AggregateException("Registry policy write failed and recovery also failed.", writeError, recoveryError);
                throw;
            }
            finally
            {
                TryDelete(temporary);
                if (File.Exists(path)) TryDelete(backup);
                TryDelete(failedReplacement);
            }
        }

        private static string ValidatePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A registry policy path is required.", nameof(filePath));
            string path = Path.GetFullPath(filePath);
            string root = Path.GetPathRoot(path) ?? string.Empty;
            if (path.AsSpan(root.Length).IndexOf(':') >= 0)
                throw new ArgumentException("Registry policy paths cannot address alternate data streams.", nameof(filePath));
            EnsureNoReparseAncestors(Path.GetDirectoryName(path) ?? root);
            return path;
        }

        private static FileInfo GetRegularFile(string path)
        {
            EnsureNoReparseAncestors(Path.GetDirectoryName(path) ?? path);
            var item = new FileInfo(path);
            item.Refresh();
            if (!item.Exists) throw new FileNotFoundException("Registry policy file was not found.", path);
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Registry policy files cannot be reparse points.");
            return item;
        }

        private static void EnsureNoReparseAncestors(string path)
        {
            DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(path));
            while (current is not null)
            {
                current.Refresh();
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Registry policy paths cannot traverse reparse directories: {current.FullName}");
                current = current.Parent;
            }
        }

        private static long ValidateEntries(IEnumerable<PolEntry> entries)
        {
            long totalData = 0;
            long serialized = PRegHeader.Length;
            foreach (PolEntry entry in entries)
            {
                ArgumentNullException.ThrowIfNull(entry);
                ValidateString(entry.KeyName, nameof(entry.KeyName));
                ValidateString(entry.ValueName, nameof(entry.ValueName));
                ArgumentNullException.ThrowIfNull(entry.Data);
                if (!Enum.IsDefined(typeof(RegistryValueKind), entry.Type))
                    throw new ArgumentOutOfRangeException(nameof(entry.Type), entry.Type, "Unsupported registry value type.");
                if (entry.Data.Length > MaximumEntryDataBytes)
                    throw new ArgumentOutOfRangeException(nameof(entry.Data), $"Entry data exceeds {MaximumEntryDataBytes} bytes.");
                totalData = checked(totalData + entry.Data.Length);
                if (totalData > MaximumTotalDataBytes)
                    throw new ArgumentOutOfRangeException(nameof(entries), $"Entry data exceeds {MaximumTotalDataBytes} bytes in aggregate.");
                serialized = checked(serialized + 20L + (entry.KeyName.Length + entry.ValueName.Length + 2L) * sizeof(char) + entry.Data.Length);
                if (serialized > MaximumFileBytes)
                    throw new ArgumentOutOfRangeException(nameof(entries), $"Serialized registry policy exceeds {MaximumFileBytes} bytes.");
            }
            return serialized;
        }

        private static void ValidateString(string value, string name)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length > MaximumStringCharacters)
                throw new ArgumentOutOfRangeException(name, $"Registry policy strings cannot exceed {MaximumStringCharacters} characters.");
            if (value.IndexOf('\0') >= 0)
                throw new ArgumentException("Registry policy strings cannot contain embedded null characters.", name);
        }

        private static void RequireCharacter(BinaryReader reader, char expected, string purpose)
        {
            char observed;
            try { observed = reader.ReadChar(); }
            catch (EndOfStreamException error)
            {
                throw new EndOfStreamException($"Registry policy ended before the {purpose}.", error);
            }
            if (observed != expected)
                throw new InvalidDataException($"Invalid registry policy {purpose}: expected '{expected}', observed '{observed}'.");
        }

        private static string ReadNullTerminatedString(BinaryReader reader, string purpose)
        {
            var value = new StringBuilder();
            while (value.Length <= MaximumStringCharacters)
            {
                char character;
                try { character = reader.ReadChar(); }
                catch (EndOfStreamException error)
                {
                    throw new EndOfStreamException($"Registry policy ended inside the {purpose}.", error);
                }
                if (character == '\0') return value.ToString();
                value.Append(character);
            }
            throw new InvalidDataException($"Registry policy {purpose} exceeds {MaximumStringCharacters} characters.");
        }

        private static void WriteNullTerminatedString(BinaryWriter writer, string value)
        {
            foreach (char character in value) writer.Write(character);
            writer.Write('\0');
        }

        private static bool HeaderMatches(byte[] header)
        {
            for (int index = 0; index < PRegHeader.Length; index++)
                if (header[index] != PRegHeader[index]) return false;
            return true;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
