namespace Legacy.Maliev.Web;

internal sealed record BuildIdentity(string Repository, string Branch, string Commit)
{
    private const string Unknown = "unknown";

    internal static BuildIdentity FromConfiguration(IConfiguration configuration) => new(
        Normalize(configuration["BuildIdentity:Repository"]),
        Normalize(configuration["BuildIdentity:Branch"]),
        Normalize(configuration["BuildIdentity:Commit"]));

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim();
}
