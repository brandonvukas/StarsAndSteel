namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// Marks a test that needs Docker. If the daemon isn't reachable, xUnit reports
/// the test as Skipped rather than Failed. CI environments must have Docker.
/// </summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerAvailability.IsAvailable)
        {
            Skip = "Docker is not available on this machine; skipping Testcontainers-based test.";
        }
    }
}
