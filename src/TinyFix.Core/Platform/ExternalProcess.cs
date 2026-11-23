// Runs a command-line helper (xclip, secret-tool, security, osascript...) and returns its output. Used by the Linux
// and macOS adapters. Arguments are passed as a list (never through a shell), so a key or a text with quotes or
// spaces cannot be mistaken for more arguments.

using System.Diagnostics;
using System.Text;

namespace TinyFix.Core.Platform;

/// <summary>The result of a helper process.</summary>
/// <param name="ExitCode">The exit code, or -1 when the program could not be started or timed out.</param>
public readonly record struct ProcessResult(int ExitCode, string Output)
{
    /// <summary>True when the program ran and exited with 0.</summary>
    public bool Success => ExitCode == 0;
}

/// <summary>Small wrapper over <see cref="Process"/>.</summary>
public static class ExternalProcess
{
    /// <summary>Runs <paramref name="fileName"/> with <paramref name="arguments"/>, optionally feeding <paramref name="input"/> to its stdin.</summary>
    public static ProcessResult Run(string fileName, IEnumerable<string> arguments, string? input = null, TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (name, value) in environment)
                start.Environment[name] = value;
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return new ProcessResult(-1, "");
            if (input is not null)
            {
                process.StandardInput.Write(input);
                process.StandardInput.Close();
            }
            var output = process.StandardOutput.ReadToEndAsync();
            process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout ?? TimeSpan.FromSeconds(5)))
            {
                process.Kill(entireProcessTree: true);
                return new ProcessResult(-1, "");
            }
            return new ProcessResult(process.ExitCode, output.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // The helper is not installed.
            return new ProcessResult(-1, "");
        }
    }

    /// <summary>True when <paramref name="program"/> can be started (found on PATH).</summary>
    public static bool Exists(string program)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, program)) || File.Exists(Path.Combine(dir, program + ".exe")));
    }
}
