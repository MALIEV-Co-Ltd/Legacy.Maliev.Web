extern alias AuthApi;
using AuthProgram = AuthApi::Program;
using System.Net;
using Legacy.Maliev.AuthService.Application;
using Legacy.Maliev.AuthService.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Maliev.FxAuthHost;

public static class NativeAuthHost
{
    public static async Task Main()
    {
        if (!Guid.TryParse(Required("MALIEV_FX_RUN_ID"), out var run))
            throw new InvalidOperationException("Disposable graph UUID required.");
        var port = int.Parse(Required("MALIEV_FX_AUTH_PORT"), System.Globalization.CultureInfo.InvariantCulture);
        if (port <= 1024 || port > 65535) throw new InvalidOperationException("Ephemeral loopback port required.");
        string Connection(string store)
        {
            var value = Required("ConnectionStrings__" + store);
            var parsed = new NpgsqlConnectionStringBuilder(value);
            if (parsed.Host != "127.0.0.1" || parsed.Port <= 1024
                || parsed.Database != "fx_" + run.ToString("N") + "_" + store.ToLowerInvariant())
                throw new InvalidOperationException("Only graph-owned loopback PostgreSQL databases permitted.");
            return value;
        }
        // Actual producer migrations, isolated stores, no identity or permission seeding.
        await using (var db = new CustomerIdentityDbContext(new DbContextOptionsBuilder<CustomerIdentityDbContext>()
            .UseNpgsql(Connection("CustomerIdentity")).Options)) await db.Database.MigrateAsync();
        await using (var db = new EmployeeIdentityDbContext(new DbContextOptionsBuilder<EmployeeIdentityDbContext>()
            .UseNpgsql(Connection("EmployeeIdentity")).Options)) await db.Database.MigrateAsync();
        await using (var db = new RefreshSessionDbContext(new DbContextOptionsBuilder<RefreshSessionDbContext>()
            .UseNpgsql(Connection("RefreshSessions")).Options)) await db.Database.MigrateAsync();
        await using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        factory.StartServer();
        var configured = factory.Services.GetRequiredService<IOptions<ServiceClientOptions>>().Value;
        if (configured.Clients.Values.Any(client => client.Permissions.Contains(
                LegacyAccessTokenPermissions.CatalogCurrenciesRead, StringComparer.Ordinal)))
            throw new InvalidOperationException("Fixture currency-read assignment is forbidden.");
        var issuer = factory.Services.GetRequiredService<IServiceAccessTokenIssuer>();
        if (issuer.GetType() != typeof(RsaAccessTokenIssuer))
            throw new InvalidOperationException("Unchanged normal server issuer required.");
        Console.WriteLine("Native Auth Program ready on UUID-owned loopback graph.");
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, args) => { args.Cancel = true; stop.Cancel(); };
        try { await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException("Disposable native host input required: " + name);

    private sealed class Factory : WebApplicationFactory<AuthProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseContentRoot(Required("MALIEV_FX_AUTH_CONTENT_ROOT"));
            if (Environment.GetEnvironmentVariable("MALIEV_FX_AUTH_CLOCK") == "expired")
            {
                // Expired-token control changes only time supplied to the actual normal issuer.
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new ExpiredClock(DateTimeOffset.UtcNow.AddHours(-1)));
                });
            }
        }
    }
    private sealed class ExpiredClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
