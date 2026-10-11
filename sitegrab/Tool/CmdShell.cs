// language: C#, file: CmdShell.cs

using System.Diagnostics;
using System.Text;

namespace SiteGrabber;

public class CmdShell
{
    public event Action<string>? OnOutput;
    public event Action<string>? OnError;

    private Process? _proc;

    public bool IsRunning => _proc is { HasExited: false };

    public void Run(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return;
        if (IsRunning) { OnError?.Invoke("a command is already running"); return; }

        var psi = new ProcessStartInfo
        {
            FileName               = "cmd.exe",
            Arguments              = "/c " + commandLine,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };

        _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _proc.OutputDataReceived += (_, e) => { if (e.Data != null) OnOutput?.Invoke(e.Data); };
        _proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) OnError?.Invoke(e.Data); };
        _proc.Exited             += (_, _) => OnOutput?.Invoke($"--- exit code {_proc?.ExitCode}");

        try
        {
            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"failed to start: {ex.Message}");
        }
    }

    public void Kill()
    {
        try { if (IsRunning) _proc?.Kill(entireProcessTree: true); }
        catch { }
    }
}