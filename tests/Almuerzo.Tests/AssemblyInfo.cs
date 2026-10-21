// SQL Server's EnsureDeletedAsync clears connection pools for the entire test process.
// Serialize fixtures so teardown cannot interfere with another test's live database.
// https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.SqlServer/Storage/Internal/SqlServerDatabaseCreator.cs
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
