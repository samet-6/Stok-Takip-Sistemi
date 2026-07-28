using Xunit;

namespace StokTakip.IntegrationTests;

/// <summary>
/// Every integration test joins this collection, so the database is dropped and migrated once
/// per run instead of once per test class. It also serialises the classes against each other:
/// they share one database, and xunit would otherwise run them in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<TestDatabaseFixture>
{
    public const string Name = "database";
}
