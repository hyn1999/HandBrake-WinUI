using System;
using System.IO;
using System.Text.Json;
using HandBrake.Interop.Interop.Json.Encode;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private static string DiagnosticsFolder => Path.Combine(Path.GetDirectoryName(AppPreferences.StorageFile)!, "diagnostics");

    private static void SaveEncodeJobDiagnostic(JsonEncodeObject job)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsFolder);
            File.WriteAllText(Path.Combine(DiagnosticsFolder, "last-encode-job.json"),
                JsonSerializer.Serialize(job, new JsonSerializerOptions { WriteIndented = true }));
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
            File.WriteAllText(path, $"{DateTimeOffset.Now:O}\n{message}\n{error}");
            return message + " Diagnostic: " + path;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return message + " (Could not save diagnostic: " + failure.Message + ")";
        }
    }
}
