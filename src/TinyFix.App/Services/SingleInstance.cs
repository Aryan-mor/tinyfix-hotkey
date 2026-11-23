// Keeps one TinyFix per user. A later launch forwards its arguments through a named pipe and exits; the first
// instance answers by opening Settings. Adapted from the reference project's single-instance helper (MIT, credited in THIRD-PARTY-NOTICES.md),
// reduced to what TinyFix needs.

using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TinyFix.Core.Settings;

namespace TinyFix.App.Services;

/// <summary>Named mutex plus named pipe single-instance helper.</summary>
public sealed class SingleInstance : IDisposable
{
    // string.GetHashCode() is randomized per process, so a stable hash is needed for both processes to agree.
    internal static readonly string DefaultName = NameFor(Environment.UserName, DataDirectory.Override);

    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex, string name, bool isFirst)
    {
        _mutex = mutex;
        _name = name;
        IsFirst = isFirst;
    }

    /// <summary>True when this process owns the lock.</summary>
    public bool IsFirst { get; }

    /// <summary>Raised on a background thread with the arguments of a later launch.</summary>
    public event Action<string[]>? ArgumentsReceived;

    /// <summary>
    /// The mutex and pipe name for a user. A copy started with TINYFIX_DATA_DIR gets a name of its own, so it neither
    /// forwards to nor receives from the TinyFix the user runs every day.
    /// </summary>
    internal static string NameFor(string userName, string? dataDirectoryOverride)
    {
        var key = dataDirectoryOverride is null ? userName : userName + "|" + dataDirectoryOverride.ToUpperInvariant();
        return "TinyFix-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12];
    }

    /// <summary>Takes the lock under the default name.</summary>
    public static SingleInstance Acquire() => Acquire(DefaultName);

    /// <summary>Takes the lock under another name (tests, which must not reach a TinyFix that is running).</summary>
    internal static SingleInstance Acquire(string name)
    {
        bool createdNew;
        var mutex = OperatingSystem.IsWindows()
            ? new Mutex(initiallyOwned: true, @"Local\" + name, out createdNew)
            : new Mutex(initiallyOwned: true, name, new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false }, out createdNew);
        return new SingleInstance(mutex, name, createdNew);
    }

    /// <summary>Starts listening for later launches (first instance only).</summary>
    public void StartListening()
    {
        if (!IsFirst)
            return;
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                    using var reading = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    reading.CancelAfter(TimeSpan.FromSeconds(10));
                    var args = await JsonSerializer.DeserializeAsync(server, AppJsonContext.Default.StringArray, reading.Token).ConfigureAwait(false);
                    if (args is not null)
                        ArgumentsReceived?.Invoke(args);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or UnauthorizedAccessException)
                {
                    // A client disconnected half-way or sent nothing usable: keep serving, after a moment so a
                    // persistent failure cannot spin the CPU.
                    try
                    {
                        await Task.Delay(200, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        });
    }

    /// <summary>
    /// Sends this process's arguments to the first instance, trying again until <paramref name="wait"/> has passed
    /// (the first instance may still be starting). False when it never answered.
    /// </summary>
    public bool ForwardToFirst(string[] args, TimeSpan wait)
    {
        var message = JsonSerializer.SerializeToUtf8Bytes(args, AppJsonContext.Default.StringArray);
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(500);
                client.Write(message);
                client.Flush();
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                    return false;
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>Stops listening and releases the lock.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            if (IsFirst)
                _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from another thread than the one that took it: the OS frees it at exit anyway.
        }
        _mutex.Dispose();
        _cts.Dispose();
    }
}
