using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.Interfaces.EventArgs;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private static readonly object NativeLogLock = new();
    private static readonly Queue<string> RecentNativeMessages = new();

    private static void BeginEncodeDiagnostic()
    {
        lock (NativeLogLock) RecentNativeMessages.Clear();
        try
        {
            Directory.CreateDirectory(DiagnosticsFolder);
            File.Delete(Path.Combine(DiagnosticsFolder, "last-encode-job.json"));
            File.Delete(Path.Combine(DiagnosticsFolder, "last-encode-error.txt"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("Unable to reset encode diagnostics: " + error.Message);
        }
    }

    private void InitializeNativeDiagnostics()
    {
        HandBrakeUtils.MessageLogged += NativeMessageLogged;
        HandBrakeUtils.ErrorLogged += NativeMessageLogged;
        Closed += (_, _) =>
        {
            HandBrakeUtils.MessageLogged -= NativeMessageLogged;
            HandBrakeUtils.ErrorLogged -= NativeMessageLogged;
        };
    }

    private static void NativeMessageLogged(object? sender, MessageLoggedEventArgs args)
    {
        try
        {
            lock (NativeLogLock)
            {
                RecentNativeMessages.Enqueue(args.Message);
                while (RecentNativeMessages.Count > 100) RecentNativeMessages.Dequeue();
                Directory.CreateDirectory(DiagnosticsFolder);
                string path = Path.Combine(DiagnosticsFolder, "native.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                string line = $"{DateTimeOffset.Now:O} {args.Message}{Environment.NewLine}";
                File.AppendAllText(path, line);
                if (activeQueueLogPath is { } jobPath)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(jobPath)!);
                    if (File.Exists(jobPath) && new FileInfo(jobPath).Length > 2 * 1024 * 1024)
                        File.Move(jobPath, jobPath + ".previous", overwrite: true);
                    File.AppendAllText(jobPath, line);
                }
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("Native diagnostic unavailable: " + failure.Message);
        }
    }

    private static string DiagnosticsFolder => Path.Combine(Path.GetDirectoryName(AppPreferences.StorageFile)!, "diagnostics");

    private static void SaveEncodeJobDiagnostic(string job)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsFolder);
            File.WriteAllText(Path.Combine(DiagnosticsFolder, "last-encode-job.json"),
                job);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            System.Diagnostics.Debug.WriteLine("Unable to save encode diagnostic: " + error.Message);
        }
    }

    private static string SaveEncodeFailureDiagnostic(string message, Exception? error = null)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsFolder);
            string path = Path.Combine(DiagnosticsFolder, "last-encode-error.txt");
            string recent;
            string? jobPath;
            lock (NativeLogLock)
            {
                recent = string.Join(Environment.NewLine, RecentNativeMessages);
                jobPath = activeQueueLogPath;
            }
            string report = $"{DateTimeOffset.Now:O}\n{message}\n{error}\nRecent native messages:\n{recent}";
            File.WriteAllText(path, report);
            if (jobPath is not null)
            {
                path = Path.ChangeExtension(jobPath, ".error.txt");
                File.WriteAllText(path, report);
            }
            return message + " Diagnostic: " + path;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return message + " (Could not save diagnostic: " + failure.Message + ")";
        }
    }
}
