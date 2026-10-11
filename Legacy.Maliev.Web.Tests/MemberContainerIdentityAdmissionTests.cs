using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberContainerIdentityAdmissionTests
{
    [Fact]
    public void ExactDisposableIdentity_IsAdmittedWithoutDaemonAllocation()
    {
        MemberOwnedContainers.ValidateDisposableIdentity(Graph("valid"), "owned-id", "postgres:18-alpine", "owned-run");
    }

    [Theory]
    [InlineData("wrong-id")]
    [InlineData("wrong-image")]
    [InlineData("wrong-run")]
    [InlineData("persistent-mount")]
    public void ForeignOrPersistentAllocation_IsRejectedBeforeAnyMutation(string mutation)
    {
        Assert.Throws<InvalidOperationException>(() => MemberOwnedContainers.ValidateDisposableIdentity(
            Graph(mutation), "owned-id", "postgres:18-alpine", "owned-run"));
    }

    private static JsonElement Graph(string mutation) => JsonSerializer.SerializeToElement(new
    {
        ID = mutation == "wrong-id" ? "foreign-id" : "owned-id",
        Config = new
        {
            Image = mutation == "wrong-image" ? "other:latest" : "postgres:18-alpine",
            Labels = new Dictionary<string, string>
            {
                ["maliev.task"] = "web-476-member-authority",
                ["maliev.run"] = mutation == "wrong-run" ? "foreign-run" : "owned-run",
                ["maliev.disposable"] = "true",
            },
        },
        Mounts = new[] { new { Type = mutation == "persistent-mount" ? "volume" : "tmpfs" } },
    });
}
