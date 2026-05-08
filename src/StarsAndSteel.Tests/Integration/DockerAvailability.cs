using System.Diagnostics;

namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// Detects whether a Docker daemon is reachable from this machine. Used by
/// integration tests to <see cref="Skip.IfNot(bool, string)"/>-style guard so
/// the suite stays green on developer boxes without Docker installed (CI must
/// run with Docker).
/// </summary>
internal static class DockerAvailability
{
    private static readonly Lazy<bool> _isAvailable = new(Probe);

    public static bool IsAvailable => _isAvailable.Value;

    private static bool Probe()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info --format \"{{.ServerVersion}}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start()) return false;
            if (!process.WaitForExit(milliseconds: 3000))
            {
                try { process.Kill(); } catch { /* best effort */ }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
