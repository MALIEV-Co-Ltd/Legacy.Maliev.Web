using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Isolates only the optional physical driver in explicitly selected Printing browser fixtures.</summary>
internal static class OptionalPrintingDriverFixture
{
    private const string UnavailableDriverScript = """
        (() => {
            const driver = 'https://127.51.68.120:8181/3dconnexion/nlproxy';
            const applicationFetch = globalThis.fetch.bind(globalThis);
            globalThis.fetch = (input, options) => input === driver && (options?.method ?? 'GET') === 'GET'
                ? Promise.resolve(new Response(null, { status: 503 }))
                : applicationFetch(input, options);
        })();
        """;

    internal static Task UseUnavailableDriverAsync(IPage page) => page.AddInitScriptAsync(UnavailableDriverScript);
}
