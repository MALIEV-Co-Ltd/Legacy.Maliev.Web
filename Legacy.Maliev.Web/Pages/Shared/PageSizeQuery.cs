using System.Globalization;

namespace Legacy.Maliev.Web.Pages.Shared;

internal static class PageSizeQuery
{
    internal const int Default = 25;

    internal const int Maximum = 100;

    internal static bool TryParse(string? value, out int pageSize) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out pageSize) &&
        pageSize is >= 1 and <= Maximum;

    internal static bool TryResolve(int? value, out int pageSize)
    {
        pageSize = value ?? Default;
        return !value.HasValue || pageSize is >= 1 and <= Maximum;
    }

    internal static bool TryResolve(string? value, out int pageSize)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            pageSize = Default;
            return true;
        }

        return TryParse(value, out pageSize);
    }
}
