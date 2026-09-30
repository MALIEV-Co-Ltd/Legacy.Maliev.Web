using Legacy.Maliev.CustomerService.Data;
using Legacy.Maliev.CustomerService.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// The test fixture owns the container and passes this connection only to the child process.
// Refuse conventional/shared database names and non-loopback connections before any DDL.
var connection = Environment.GetEnvironmentVariable("MALIEV_PROFILE_DISPOSABLE_CONNECTION")
    ?? throw new InvalidOperationException("The disposable fixture connection is required.");
var expectedDatabase = Environment.GetEnvironmentVariable("MALIEV_PROFILE_DISPOSABLE_DATABASE");
var parsed = new NpgsqlConnectionStringBuilder(connection);
if (parsed.Host is not ("127.0.0.1" or "localhost") || parsed.Port <= 1024
    || expectedDatabase is null || !expectedDatabase.StartsWith("profile_contract_", StringComparison.Ordinal)
    || !Guid.TryParseExact(expectedDatabase["profile_contract_".Length..], "N", out _)
    || !string.Equals(parsed.Database, expectedDatabase, StringComparison.Ordinal))
    throw new InvalidOperationException("Only a fixture-owned loopback disposable database is permitted.");

await using var db = new CustomerDbContext(new DbContextOptionsBuilder<CustomerDbContext>().UseNpgsql(connection).Options);
await db.Database.MigrateAsync();
var company = new Company { Name = "Stored company" };
var billing = new Address { AddressLine1 = "Stored billing", AddressLine2 = "Saved billing district", City = "Bangkok", State = "Bangkok", PostalCode = "10110", CountryId = 66 };
var shipping = new Address { AddressLine1 = "Distinct shipping", AddressLine2 = "Saved shipping district", City = "Chiang Mai", State = "Chiang Mai", PostalCode = "50000", CountryId = 66 };
var owner = new Customer { FirstName = "Owner", LastName = "Name", Company = company, BillingAddress = billing, ShippingAddress = shipping };
var other = new Customer { FirstName = "Other", LastName = "Owner", Email = "other@example.test", Company = company, BillingAddress = billing, ShippingAddress = shipping };
db.AddRange(owner, other);
await db.SaveChangesAsync();
if (owner.Id != 1 || other.Id != 2) throw new InvalidOperationException("Unexpected disposable seed identities.");
Console.WriteLine("Disposable profile contract schema and owner graphs are ready.");
