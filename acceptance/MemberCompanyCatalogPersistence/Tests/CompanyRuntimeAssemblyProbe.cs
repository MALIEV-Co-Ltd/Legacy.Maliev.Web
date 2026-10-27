using Xunit;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Runs the shared no-backend observer inside the Company test host.</summary>
public sealed class CompanyRuntimeAssemblyProbe
{
    [Fact]
    public Task ObserveHttpAssemblyWithoutBackend() => RuntimeAssemblyProbe.ObserveCore(typeof(CompanyRuntimeAssemblyProbe).Assembly);
}
