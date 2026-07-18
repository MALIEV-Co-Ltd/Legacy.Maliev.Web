namespace Legacy.Maliev.Web.Components.Pages.Account;

public sealed record LoginFormDisplayModel(
    string Email,
    bool RememberMe,
    string? ReturnUrl,
    string? Notification,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ValidationErrors)
{
    public IReadOnlyList<string> AllErrors => ValidationErrors.Values.SelectMany(errors => errors).ToArray();

    public IReadOnlyList<string> ErrorsFor(string fieldName) =>
        ValidationErrors.TryGetValue(fieldName, out var errors) ? errors : [];

    public string? FirstErrorFor(string fieldName) => ErrorsFor(fieldName).FirstOrDefault();
}
