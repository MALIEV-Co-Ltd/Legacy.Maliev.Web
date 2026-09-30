using Legacy.Maliev.AuthService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var connection = Environment.GetEnvironmentVariable("MALIEV_PROFILE_DISPOSABLE_CONNECTION")
    ?? throw new InvalidOperationException("Disposable fixture connection required.");
var expectedDatabase = Environment.GetEnvironmentVariable("MALIEV_PROFILE_DISPOSABLE_DATABASE");
var parsed = new NpgsqlConnectionStringBuilder(connection);
if (parsed.Host is not ("127.0.0.1" or "localhost") || parsed.Port <= 1024
    || expectedDatabase is null || !expectedDatabase.StartsWith("profile_identity_", StringComparison.Ordinal)
    || !Guid.TryParseExact(expectedDatabase["profile_identity_".Length..], "N", out _)
    || !string.Equals(parsed.Database, expectedDatabase, StringComparison.Ordinal))
    throw new InvalidOperationException("Only fixture-owned loopback disposable identity DB is permitted.");

await using var db = new CustomerIdentityDbContext(new DbContextOptionsBuilder<CustomerIdentityDbContext>().UseNpgsql(connection).Options);
await db.Database.EnsureCreatedAsync();
db.Users.AddRange(
    new LegacyIdentityRow { Id = "fixture-owner-1", DatabaseID = 1, Email = "owner@example.test", MobileNumber = "0812345678", EmailConfirmed = true },
    new LegacyIdentityRow { Id = "fixture-owner-2", DatabaseID = 2, Email = "other@example.test", MobileNumber = "0899999999", EmailConfirmed = true });
await db.SaveChangesAsync();
Console.WriteLine("Disposable owner identity projection is ready.");
