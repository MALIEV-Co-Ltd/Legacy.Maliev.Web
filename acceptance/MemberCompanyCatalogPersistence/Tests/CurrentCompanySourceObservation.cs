using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Passive current-source evidence; never configures a handler, option, or retry.</summary>
internal sealed class CurrentCompanySourceObservation
{
    private readonly WeakReference<ResilienceHandler> outer;
    private readonly WeakReference<ResilienceHandler> inner;
    private readonly JsonElement expected;
    private readonly CurrentCompanyAssembly[] assemblies;
    internal string BuildEvidenceSha256 { get; }
    internal string SourceProfileSha256 { get; }
    internal string DefaultsRevision => assemblies[0].Revision;
    internal string PackageRevision => assemblies[1].Revision;

    private CurrentCompanySourceObservation(ResilienceHandler[] handlers, HttpMessageHandlerBuilder builder)
    {
        outer = new(handlers[0]);
        inner = new(handlers[1]);
        var root = FindRoot();
        var raw = ReadBounded(Path.Combine(root, "member-company-retained", "build-source-evidence.json"), 1024 * 1024);
        BuildEvidenceSha256 = Hash(raw);
        using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 16 });
        expected = document.RootElement.Clone();
        var candidate = Environment.GetEnvironmentVariable("MALIEV_COMPANY_CANDIDATE_HEAD");
        Assert.True(Regex.IsMatch(candidate ?? string.Empty, "\\A[a-f0-9]{40}\\z"));
        Assert.Equal(candidate, expected.GetProperty("candidateHead").GetString());
        Assert.True(expected.GetProperty("sourceChecked").GetBoolean());
        SourceProfileSha256 = Hash(ReadBounded(Path.Combine(root, "acceptance", "MemberCompanyCatalogPersistence", "current-profile.json"), 1024 * 1024));
        Assert.Equal(SourceProfileSha256, expected.GetProperty("profileSha256").GetString());

        var defaults = typeof(Microsoft.Extensions.DependencyInjection.LegacyHttpResilienceExtensions).Assembly;
        Assert.Same(defaults, typeof(Microsoft.Extensions.Hosting.Extensions).Assembly);
        Assert.Same(typeof(ResilienceHandler).Assembly, handlers[0].GetType().Assembly);
        Assert.Same(handlers[0].GetType().Assembly, handlers[1].GetType().Assembly);
        Assert.Same(typeof(HttpMessageHandlerBuilder).Assembly, builder.GetType().Assembly);
        Assert.Equal("Microsoft.Extensions.Http.DefaultHttpMessageHandlerBuilder", builder.GetType().FullName);
        assemblies =
        [
            Observe("defaults", defaults, "Legacy.Maliev.ServiceDefaults", "4ffba0639620e7e923d331317f3f28501526b573"),
            Observe("resilience", handlers[0].GetType().Assembly, "Microsoft.Extensions.Http.Resilience", "25f07afa51034e37b334210642d7dc5f191c142d"),
            Observe("http", builder.GetType().Assembly, "Microsoft.Extensions.Http", "4099f83977f4d29604a9aeb971fb0a7bc1d5f699"),
            Observe("polly", typeof(Polly.ResiliencePipeline).Assembly, "Polly.Core", "077cf388fc5a85a7f6cd0fdcb2b68d17a898019e"),
        ];
        var built = expected.GetProperty("assemblies").EnumerateArray().ToArray();
        Assert.Equal(4, built.Length);
        for (var index = 0; index < assemblies.Length; index++)
        {
            var actual = assemblies[index];
            var recorded = built[index];
            Assert.Equal(actual.Role, recorded.GetProperty("role").GetString());
            Assert.Equal(actual.AssemblyName, recorded.GetProperty("assemblyName").GetString());
            Assert.Equal(actual.Revision, recorded.GetProperty("revision").GetString());
            Assert.Equal(actual.Version, recorded.GetProperty("version").GetString());
            Assert.Equal(actual.Mvid, recorded.GetProperty("mvid").GetString());
            Assert.Equal(actual.Sha256, recorded.GetProperty("sha256").GetString());
        }
    }

    internal static CurrentCompanySourceObservation Capture(HttpMessageHandlerBuilder builder)
    {
        var handlers = builder.AdditionalHandlers.OfType<ResilienceHandler>().ToArray();
        Assert.Equal(2, handlers.Length);
        Assert.All(handlers, handler => Assert.Equal(typeof(ResilienceHandler), handler.GetType()));
        return new(handlers, builder);
    }

    internal bool Equivalent(CurrentCompanySourceObservation other) =>
        BuildEvidenceSha256 == other.BuildEvidenceSha256
        && SourceProfileSha256 == other.SourceProfileSha256
        && assemblies.SequenceEqual(other.assemblies);

    internal void VerifyLinkedOrder()
    {
        Assert.True(outer.TryGetTarget(out var first));
        Assert.True(inner.TryGetTarget(out var second));
        VerifyLinkedOrder(first, second);
    }

    internal static void VerifyLinkedOrder(HttpMessageHandler? first, HttpMessageHandler? second)
    {
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        var seen = new HashSet<HttpMessageHandler>(ReferenceEqualityComparer.Instance);
        HttpMessageHandler? current = first;
        var found = false;
        for (var index = 0; current is not null && index < 64; index++)
        {
            Assert.True(seen.Add(current), "A cycle in the actual handler chain refuses proof.");
            if (ReferenceEquals(current, second))
            {
                found = true;
                break;
            }
            current = (current as DelegatingHandler)?.InnerHandler;
        }
        Assert.True(found, "The same two captured handlers must be linked in their observed outer/inner order.");
    }

    internal object[] AssemblyReceipt() => assemblies.Select(row => (object)new
    {
        role = row.Role,
        assemblyName = row.AssemblyName,
        revision = row.Revision,
        version = row.Version,
        mvid = row.Mvid,
        sha256 = row.Sha256,
    }).ToArray();

    private static CurrentCompanyAssembly Observe(string role, Assembly assembly, string name, string requiredRevision)
    {
        Assert.False(assembly.IsDynamic);
        Assert.Equal(name, assembly.GetName().Name);
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.InRange(informational?.Length ?? 0, 1, 128);
        var parts = informational!.Split('+');
        Assert.Equal(2, parts.Length);
        Assert.True(Regex.IsMatch(parts[1], "\\A[a-f0-9]{40}\\z"));
        Assert.Equal(requiredRevision, parts[1]);
        var location = assembly.Location;
        Assert.True(Path.IsPathFullyQualified(location));
        var raw = ReadBounded(location, 16 * 1024 * 1024);
        return new(role, name, parts[1], assembly.GetName().Version!.ToString(), assembly.ManifestModule.ModuleVersionId.ToString("D"), Hash(raw));
    }

    private static byte[] ReadBounded(string path, int cap)
    {
        var before = new FileInfo(path);
        Assert.Null(before.LinkTarget);
        Assert.InRange(before.Length, 1, cap);
        var length = before.Length;
        var changed = before.LastWriteTimeUtc;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var raw = new byte[checked((int)length)];
        stream.ReadExactly(raw);
        Assert.Equal(-1, stream.ReadByte());
        before.Refresh();
        Assert.Equal(length, before.Length);
        Assert.Equal(changed, before.LastWriteTimeUtc);
        return raw;
    }

    private static string Hash(byte[] raw) => Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Actual Company repository root required.");
    }
}

internal sealed record CurrentCompanyAssembly(string Role, string AssemblyName, string Revision, string Version, string Mvid, string Sha256);
