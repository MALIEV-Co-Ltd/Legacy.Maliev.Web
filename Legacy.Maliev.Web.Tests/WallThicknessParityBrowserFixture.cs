using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

// Component-only host. No CNC routes, server-admission bypass or external upload transport.
public sealed class WallThicknessParityBrowserFixture : IAsyncLifetime
{
    private TestingWebApplicationFactory? factory;
    private HttpClient? client;
    private IPlaywright? playwright;
    public IBrowser Browser { get; private set; } = null!;
    public string Url { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var origin = new Uri($"http://127.0.0.1:{port}");
        factory = new TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory());
        factory.UseKestrel(port);
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        Url = new Uri(origin, "/instantquotation/3d-printing").ToString();
        using var response = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-component=\"instant-quotation-three-dimensional-printing\"",
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        const string worker = "src/app/js/instant-quotation/wall-thickness-runner.worker.js";
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), "wwwroot", worker)),
            await client.GetByteArrayAsync("/" + worker));
        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        playwright?.Dispose();
        client?.Dispose();
        if (factory is not null) await factory.DisposeAsync();
    }

    internal static byte[] Box(float height)
    {
        float[][] points = [[0,0,0],[20,0,0],[20,20,0],[0,20,0],
            [0,0,height],[20,0,height],[20,20,height],[0,20,height]];
        int[][] faces = [[0,2,1],[0,3,2],[4,5,6],[4,6,7],[0,1,5],[0,5,4],
            [1,2,6],[1,6,5],[2,3,7],[2,7,6],[3,0,4],[3,4,7]];
        return Stl(points, faces);
    }

    // Actual .NET component rendering/lifecycle; only local exceptional state is seeded.
    // No server-admitted part, pricing, authentication or coordinator result is fabricated.
    internal static async Task<(string Before, string After, string? Key, string[] JsCalls)> RenderLifecycleAsync(string operation)
    {
        var localId = Guid.NewGuid();
        var reference = new RecordingJsReference();
        var activator = new CapturingActivator(localId, reference);
        using var services = new ServiceCollection().AddLogging()
            .AddLocalization(options => options.ResourcesPath = "Resources")
            .AddSingleton<IComponentActivator>(activator)
            .AddSingleton<IJSRuntime, NullJsRuntime>()
            .AddSingleton<IWebHostEnvironment>(new ComponentEnvironment())
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["InitialState"] = InstantQuotationWorkflowState.Error }));
            var before = output.ToHtmlString();
            var component = activator.Component!;
            var method = typeof(InstantQuotationWorkflow).GetMethod(operation, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            var invoked = method.Invoke(component, operation == "DisposeAsync" ? [] : [localId]);
            if (invoked is Task task) await task;
            if (invoked is ValueTask valueTask) await valueTask;
            await component.ReportIncompletePreviewAsync("local-only-preview", 99, true);
            // A real event handler triggers this render automatically. HtmlRenderer has no DOM events.
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, []);
            return (before, output.ToHtmlString(), (string?)Field("incompletePreviewKey").GetValue(component), reference.Calls.ToArray());
        });
    }

    private static FieldInfo Field(string name) => typeof(InstantQuotationWorkflow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal static async Task<(bool StaleAttached, string? KeyAfterOldReply, bool ReplacementAttached, string[] RetainedKeys, string HtmlAfterOldReply)> RenderAttachmentRaceAsync(string transition)
    {
        var reference = new RecordingJsReference { ControlAttachment = true, ControlEligibility = transition.StartsWith("eligibility-", StringComparison.Ordinal) };
        var activator = new CapturingActivator(Guid.NewGuid(), reference);
        using var services = new ServiceCollection().AddLogging()
            .AddLocalization(options => options.ResourcesPath = "Resources")
            .AddSingleton<IComponentActivator>(activator).AddSingleton<IJSRuntime, NullJsRuntime>()
            .AddSingleton<IWebHostEnvironment>(new ComponentEnvironment()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["InitialState"] = InstantQuotationWorkflowState.Error }));
            var component = activator.Component!;
            Field("incompletePreviewAttached").SetValue(component, false);
            var afterRender = typeof(InstantQuotationWorkflow).GetMethod("OnAfterRenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var oldAttachment = (Task)afterRender.Invoke(component, [false])!;
            Assert.True(reference.PendingAttachments.ContainsKey("local-only-preview"));
            if (reference.ControlEligibility)
            {
                reference.PendingAttachments["local-only-preview"].SetResult(true);
                await reference.EligibilityStarted.Task;
            }
            if (transition == "ineligible") reference.Eligible = false;
            else if (transition is "dispose" or "eligibility-dispose") await component.DisposeAsync();
            else if (transition != "current-failure") await (Task)typeof(InstantQuotationWorkflow).GetMethod("RemoveIncompletePreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, [])!;
            Task? replacement = null;
            if (transition is "replacement" or "stale-failure" or "eligibility-replacement")
            {
                Field("incompletePreviewKey").SetValue(component, "replacement-preview");
                replacement = (Task)afterRender.Invoke(component, [false])!;
                Assert.True(reference.PendingAttachments.ContainsKey("replacement-preview"));
            }
            if (reference.ControlEligibility) reference.PendingEligibility.SetResult(true);
            else if (transition is "stale-failure" or "current-failure") reference.PendingAttachments["local-only-preview"].SetException(new JSException("controlled old attachment failure"));
            else reference.PendingAttachments["local-only-preview"].SetResult(true);
            await oldAttachment;
            var staleAttached = (bool)Field("incompletePreviewAttached").GetValue(component)!;
            var key = (string?)Field("incompletePreviewKey").GetValue(component);
            // OnAfterRender completion is NOT a normal event: its current cleanup
            // must render automatically, without this test manufacturing the result.
            if (transition is not ("ineligible" or "current-failure"))
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, []);
            var html = output.ToHtmlString();
            if (replacement is not null)
            {
                reference.PendingAttachments["replacement-preview"].SetResult(true);
                await replacement;
            }
            return (staleAttached, key, replacement is not null && (bool)Field("incompletePreviewAttached").GetValue(component)!, reference.RetainedKeys.ToArray(), html);
        });
    }

    private sealed class CapturingActivator(Guid localId, RecordingJsReference reference) : IComponentActivator
    {
        internal InstantQuotationWorkflow? Component { get; private set; }
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is InstantQuotationWorkflow workflow)
            {
                Component = workflow;
                Field("incompletePreviewKey").SetValue(workflow, "local-only-preview");
                Field("incompletePreviewAttached").SetValue(workflow, true);
                Field("incompletePreviewMeasured").SetValue(workflow, true);
                Field("incompletePreviewRevision").SetValue(workflow, 1L);
                Field("previewInterop").SetValue(workflow, reference);
                ((Dictionary<Guid, string>)Field("previewKeys").GetValue(workflow)!).Add(localId, "local-only-preview");
            }
            return component;
        }
    }

    private sealed class RecordingJsReference : IJSObjectReference
    {
        internal List<string> Calls { get; } = [];
        internal List<string> RetainedKeys { get; } = [];
        internal bool ControlAttachment { get; init; }
        internal bool ControlEligibility { get; init; }
        internal TaskCompletionSource<bool> EligibilityStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> PendingEligibility { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Eligible { get; set; } = true;
        internal Dictionary<string, TaskCompletionSource<bool>> PendingAttachments { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            if (ControlAttachment && identifier == "retainIncompletePreview")
            {
                var key = (string)args![0]!;
                RetainedKeys.Add(key);
                var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                PendingAttachments.Add(key, pending);
                return new ValueTask<TValue>(AwaitResultAsync<TValue>(pending.Task));
            }
            if (identifier == "isIncompletePreview")
            {
                if (ControlEligibility && (string)args![0]! == "local-only-preview")
                {
                    EligibilityStarted.TrySetResult(true);
                    return new ValueTask<TValue>(AwaitResultAsync<TValue>(PendingEligibility.Task));
                }
                return ValueTask.FromResult((TValue)(object)Eligible);
            }
            return ValueTask.FromResult(default(TValue)!);
        }
        private static async Task<TValue> AwaitResultAsync<TValue>(Task<bool> pending) => (TValue)(object)await pending;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class ComponentEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = typeof(Program).Assembly.GetName().Name!;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = BrowserHostIdentityVerifier.SourceProjectDirectory();
        public string WebRootPath { get; set; } = Path.Combine(BrowserHostIdentityVerifier.SourceProjectDirectory(), "wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    internal static byte[] Sheet() => Stl([[0, 0, 0], [20, 0, 0], [20, 20, 0], [0, 20, 0]], [[0, 2, 1], [0, 3, 2]]);

    internal static byte[] CoarseFaces()
    {
        float[][] points = [[0,0,0],[20,0,0],[20,0,2],[0,0,2],
            [0,0.75f,0],[20,0.75f,0],[20,0.75f,0.5f],[0,0.75f,0.5f],
            [0,3.5f,0],[20,3.5f,0],[20,3.5f,2],[0,3.5f,2]];
        return Stl(points, [[0, 1, 2], [0, 2, 3], [4, 6, 5], [4, 7, 6], [8, 10, 9], [8, 11, 10]]);
    }

    private static byte[] Stl(float[][] points, int[][] faces)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[80]);
        writer.Write((uint)faces.Length);
        foreach (var face in faces)
        {
            writer.Write(0f); writer.Write(0f); writer.Write(0f);
            foreach (var index in face) foreach (var value in points[index]) writer.Write(value);
            writer.Write((ushort)0);
        }
        return stream.ToArray();
    }
}
