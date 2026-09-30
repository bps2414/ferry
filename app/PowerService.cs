using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ferry;

public interface IPowerService
{
    void ShutdownThisPc();
    void PreventSleep(bool enabled);
}

/// <summary>Only the production window creates this adapter. Tests inject a fake.</summary>
public sealed class WindowsPowerService : IPowerService
{
    public void ShutdownThisPc()
    {
        // The app owns the cancellable grace period. No forced process termination (/f).
        var start = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "shutdown.exe"))
        { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/s");
        start.ArgumentList.Add("/t");
        start.ArgumentList.Add("0"); // a positive /t implicitly forces apps closed on Windows
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start shutdown.");
        if (!process.WaitForExit(5000)) throw new TimeoutException("Windows did not confirm the shutdown request within 5 seconds.");
        if (process.ExitCode != 0) throw new Win32Exception(process.ExitCode);
    }

    public void PreventSleep(bool enabled)
    {
        // Same dispatcher thread for acquisition/release; does not alter the user's power plan or display.
        if (SetThreadExecutionState(0x80000000u | (enabled ? 0x00000001u : 0)) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint SetThreadExecutionState(uint flags);
}
