using Testcontainers.MsSql;

namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// xUnit collection fixture: spins up one SQL Server container shared by every
/// test class in the "Integration" collection. Tests inside the collection get
/// the running container's connection string. Container is torn down at the end.
/// </summary>
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    public MsSqlContainer Container { get; } = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => DockerAvailability.IsAvailable
        ? Container.GetConnectionString()
        : "Server=docker-not-available;Database=ignored;";

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable)
        {
            // Don't try to start the container — every test in the collection is
            // marked [DockerFact] and will be Skipped instead of Failed.
            return;
        }
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (!DockerAvailability.IsAvailable) return;
        await Container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "Integration";
}
