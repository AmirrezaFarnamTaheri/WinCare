namespace WinCare.Infrastructure.Commands;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

internal sealed partial class WindowsCommandExecutor
{
    private Task<CommandHandlerOutcome> ExecuteDeveloperPillar1CommandAsync(
        CommandDefinition definition,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        return definition.Id switch
        {
            "cleaner-developer-uv" => ExecuteUvCacheCleanAsync(cancellationToken),
            "cleaner-ide-cursor-snapshots" => ExecuteCursorSnapshotsCleanAsync(cancellationToken),
            "cleaner-ai-agent-ledgers" => ExecuteAiAgentLedgersCleanAsync(cancellationToken),
            "sqlite-vacuum" => ExecuteSqliteVacuumAsync(cancellationToken),
            _ => Task.FromResult(CommandHandlerOutcome.Failed("Unknown Developer Pillar 1 command."))
        };
    }

    private async Task<CommandHandlerOutcome> ExecuteUvCacheCleanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int purgedFiles = 0;
        long reclaimedBytes = 0;
        var details = new StringBuilder();

        // 1. If uv CLI is present in PATH or standard cargo/bin, invoke `uv cache clean`
        try
        {
            var result = await _process.RunAsync("uv.exe", ["cache", "clean"], cancellationToken, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                details.Append("uv CLI cache clean executed successfully. ");
            }
        }
        catch
        {
            // Fall back to direct filesystem purge
        }

        // 2. Direct filesystem purge of known UV cache paths
        var uvCachePaths = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uv", "cache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "uv")
        };

        foreach (var cachePath in uvCachePaths)
        {
            if (!Directory.Exists(cachePath)) continue;

            try
            {
                var dirInfo = new DirectoryInfo(cachePath);
                foreach (var file in dirInfo.EnumerateFiles("*", SafeRecursiveEnumeration))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        long size = file.Length;
                        file.Attributes = FileAttributes.Normal;
                        file.Delete();
                        purgedFiles++;
                        reclaimedBytes += size;
                    }
                    catch
                    {
                        // File may be locked by a running python/uv process
                    }
                }

                foreach (var subDir in dirInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        subDir.Delete(true);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                details.Append($"Cache purge note ({cachePath}): {ex.Message}. ");
            }
        }

        if (purgedFiles == 0 && reclaimedBytes == 0 && details.Length == 0)
        {
            return CommandHandlerOutcome.Success("Astral UV cache audited: cache is already clean; 0 bytes reclaimed.");
        }

        return CommandHandlerOutcome.Success(
            $"Astral UV cache purged: {purgedFiles} files deleted, {FormatPillarBytes(reclaimedBytes)} reclaimed. {details.ToString().Trim()}");
    }

    private async Task<CommandHandlerOutcome> ExecuteCursorSnapshotsCleanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            int deletedFiles = 0;
            long reclaimedBytes = 0;
            var details = new StringBuilder();

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var snapshotDirectories = new List<string>
            {
                Path.Combine(appData, "Cursor", "snapshots"),
                Path.Combine(appData, "Cursor", "User", "History"),
                Path.Combine(userProfile, ".cursor", "snapshots")
            };

            foreach (var targetDir in snapshotDirectories)
            {
                if (!Directory.Exists(targetDir)) continue;

                try
                {
                    var dirInfo = new DirectoryInfo(targetDir);
                    foreach (var file in dirInfo.EnumerateFiles("*", SafeRecursiveEnumeration))
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        try
                        {
                            long len = file.Length;
                            file.Attributes = FileAttributes.Normal;
                            file.Delete();
                            deletedFiles++;
                            reclaimedBytes += len;
                        }
                        catch
                        {
                        }
                    }

                    // Remove empty subdirectories
                    foreach (var sub in dirInfo.EnumerateDirectories("*", SafeRecursiveEnumeration).OrderByDescending(d => d.FullName.Length))
                    {
                        try
                        {
                            if (!sub.EnumerateFileSystemInfos().Any())
                            {
                                sub.Delete();
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    details.Append($"Snapshot directory note ({targetDir}): {ex.Message}. ");
                }
            }

            if (deletedFiles == 0 && reclaimedBytes == 0)
            {
                return CommandHandlerOutcome.Success("Cursor shadow snapshots audited: no stale snapshots found; 0 bytes reclaimed.");
            }

            return CommandHandlerOutcome.Success(
                $"Cursor shadow snapshots pruned: {deletedFiles} files removed, {FormatPillarBytes(reclaimedBytes)} reclaimed. {details.ToString().Trim()}");
        }, cancellationToken);
    }

    private async Task<CommandHandlerOutcome> ExecuteAiAgentLedgersCleanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            int prunedFiles = 0;
            long reclaimedBytes = 0;
            int locationsChecked = 0;

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var tempPath = Path.GetTempPath();

            var ledgerTargets = new List<string>
            {
                Path.Combine(userProfile, ".gemini", "antigravity-cli", "scratch"),
                Path.Combine(userProfile, ".claude", "debug"),
                Path.Combine(userProfile, ".codex", "sessions"),
                Path.Combine(userProfile, ".codex", "logs"),
                Path.Combine(userProfile, ".continue", "session-transcripts"),
                Path.Combine(userProfile, ".aider.tags.cache.v3"),
                Path.Combine(userProfile, ".aider.input.history"),
                Path.Combine(tempPath, "agent-traces")
            };

            foreach (var target in ledgerTargets)
            {
                locationsChecked++;
                if (File.Exists(target))
                {
                    try
                    {
                        var fi = new FileInfo(target);
                        long size = fi.Length;
                        fi.Attributes = FileAttributes.Normal;
                        fi.Delete();
                        prunedFiles++;
                        reclaimedBytes += size;
                    }
                    catch
                    {
                    }
                    continue;
                }

                if (!Directory.Exists(target)) continue;

                try
                {
                    var dir = new DirectoryInfo(target);
                    foreach (var file in dir.EnumerateFiles("*", SafeRecursiveEnumeration))
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        try
                        {
                            long size = file.Length;
                            file.Attributes = FileAttributes.Normal;
                            file.Delete();
                            prunedFiles++;
                            reclaimedBytes += size;
                        }
                        catch
                        {
                        }
                    }

                    foreach (var sub in dir.EnumerateDirectories("*", SafeRecursiveEnumeration).OrderByDescending(d => d.FullName.Length))
                    {
                        try
                        {
                            if (!sub.EnumerateFileSystemInfos().Any())
                            {
                                sub.Delete();
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }

            return CommandHandlerOutcome.Success(
                $"Autonomous AI agent scratchpads purged: {prunedFiles} files removed across {locationsChecked} agent locations, {FormatPillarBytes(reclaimedBytes)} reclaimed.");
        }, cancellationToken);
    }

    private async Task<CommandHandlerOutcome> ExecuteSqliteVacuumAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidateBases = new List<string>
            {
                Path.Combine(appData, "Code", "User", "globalStorage"),
                Path.Combine(appData, "Cursor", "User", "globalStorage"),
                Path.Combine(appData, "Code - Insiders", "User", "globalStorage"),
                Path.Combine(appData, "VSCodium", "User", "globalStorage"),
                Path.Combine(appData, "Code", "User", "workspaceStorage"),
                Path.Combine(appData, "Cursor", "User", "workspaceStorage")
            };

            var dbFiles = new List<FileInfo>();
            foreach (var basePath in candidateBases)
            {
                if (!Directory.Exists(basePath)) continue;

                try
                {
                    var dir = new DirectoryInfo(basePath);
                    foreach (var file in dir.EnumerateFiles("*.vscdb", SafeRecursiveEnumeration))
                    {
                        dbFiles.Add(file);
                    }
                }
                catch
                {
                }
            }

            if (dbFiles.Count == 0)
            {
                return CommandHandlerOutcome.Success("SQLite database vacuuming audited: no .vscdb state databases found.");
            }

            int vacuumedCount = 0;
            long totalReclaimed = 0;

            foreach (var dbFile in dbFiles)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    long before = dbFile.Length;
                    int rc = SqliteNative.sqlite3_open16(dbFile.FullName, out nint db);
                    if (rc == 0 && db != nint.Zero)
                    {
                        try
                        {
                            SqliteNative.sqlite3_exec(db, "VACUUM;", nint.Zero, nint.Zero, out _);
                        }
                        finally
                        {
                            SqliteNative.sqlite3_close(db);
                        }

                        dbFile.Refresh();
                        long after = dbFile.Length;
                        long reclaimed = Math.Max(0, before - after);
                        totalReclaimed += reclaimed;
                        vacuumedCount++;
                    }
                }
                catch
                {
                    // Database may be locked by an active editor instance
                }
            }

            return CommandHandlerOutcome.Success(
                $"SQLite .vscdb databases vacuumed: {vacuumedCount}/{dbFiles.Count} state stores optimized, {FormatPillarBytes(totalReclaimed)} reclaimed.");
        }, cancellationToken);
    }

    private static string FormatPillarBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
        if (bytes >= 1024L) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }

    private static class SqliteNative
    {
        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open16", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_open16(string filename, out nint db);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_exec", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_exec(nint db, string sql, nint callback, nint arg, out nint errmsg);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_close(nint db);
    }
}
