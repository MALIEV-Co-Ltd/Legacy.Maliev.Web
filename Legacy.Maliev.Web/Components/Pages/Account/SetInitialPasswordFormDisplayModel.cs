namespace Legacy.Maliev.Web.Components.Pages.Account;

public sealed record SetInitialPasswordFormDisplayModel(
    string Email,
    string Token,
    string? ReturnUrl,
    bool RememberMe,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ValidationErrors)
{
    public IReadOnlyList<string> ErrorsFor(string fieldName) =>
        ValidationErrors.TryGetValue(fieldName, out var errors) ? errors : [];

    public string? FirstErrorFor(string fieldName) => ErrorsFor(fieldName).FirstOrDefault();
}
