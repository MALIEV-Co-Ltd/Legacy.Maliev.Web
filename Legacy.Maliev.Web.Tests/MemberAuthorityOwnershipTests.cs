using System.Diagnostics;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Checks exact child custody and all-attempt cleanup without changing authentication authority.</summary>
public sealed class MemberAuthorityOwnershipTests
{
    /// <summary>Every cleanup runs and retains the original failure before all secondary failures.</summary>
    /// <returns>A task completing the real cleanup orchestration assertions.</returns>
    [Fact]
    public async Task CleanupFailure_AttemptsEveryResourceAndPreservesPrimary()
    {
        var primary = new InvalidOperationException("controlled-primary");
        var first = new IOException("controlled-first-cleanup");
        var last = new IOException("controlled-last-cleanup");
        var attempts = new List<int>();
        var failure = await Record.ExceptionAsync(() => MemberAuthorityCleanup.CompleteAsync(primary,
        [
            () => { attempts.Add(1); return Task.FromException(first); },
            () => { attempts.Add(2); return Task.CompletedTask; },
            () => { attempts.Add(3); return Task.FromException(last); },
        ]));

        var aggregate = Assert.IsType<AggregateException>(failure);
        Assert.Equal(new[] { 1, 2, 3 }, attempts);
        Assert.Collection(aggregate.InnerExceptions,
            item => Assert.Same(primary, item),
            item => Assert.Same(first, item),
            item => Assert.Same(last, item));
    }

    /// <summary>Successful cleanup does not replace an original startup exception.</summary>
    /// <returns>A task completing exception-identity assertions.</returns>
    [Fact]
    public async Task SuccessfulCleanup_RethrowsExactPrimary()
    {
        var primary = new InvalidOperationException("controlled-primary");
        var failure = await Record.ExceptionAsync(() => MemberAuthorityCleanup.CompleteAsync(
            primary, [() => Task.CompletedTask]));

        Assert.Same(primary, failure);
    }

    /// <summary>An unstarted lease can be released repeatedly without acquiring or signalling a process.</summary>
    /// <returns>A task completing idempotent unstarted cleanup.</returns>
    [Fact]
    public async Task UnstartedChild_DisposesWithoutProcessOrSignal()
    {
        var child = new MemberAuthorityChild();

        await child.DisposeAsync();
        await child.DisposeAsync();

        Assert.True(child.IsClosed);
        Assert.False(child.WasStarted);
    }

    /// <summary>A failed executable launch leaves no started child and still releases the owned process object.</summary>
    /// <returns>A task completing the actual failed-launch cleanup assertions.</returns>
    [Fact]
    public async Task FailedLaunch_DisposesUnstartedCustody()
    {
        var info = new ProcessStartInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "absent-member-probe"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var child = new MemberAuthorityChild();
        Exception? primary = null;
        try { Assert.NotNull(Record.Exception(() => child.Start(info))); }
        catch (Exception exception) { primary = exception; }
        await MemberAuthorityCleanup.CompleteAsync(primary, [() => child.DisposeAsync().AsTask()]);

        Assert.False(child.WasStarted);
        Assert.True(child.IsClosed);
    }

    /// <summary>Actual retained child custody survives rejection controls and settles both output readers.</summary>
    /// <param name="rejection">The exact ownership observation to invalidate, or natural exit.</param>
    /// <returns>A task completing the real disposable probe and exact-exit assertions.</returns>
    [Theory]
    [InlineData("borrowed")]
    [InlineData("pid-reuse")]
    [InlineData("birth")]
    [InlineData("executable")]
    [InlineData("already-exited")]
    public async Task ExactOwnedChild_RejectsForeignIdentityAndSettlesNaturalExit(string rejection)
    {
        var dll = Environment.GetEnvironmentVariable("MALIEV_CNC_LEASE_PROBE_DLL");
        Assert.True(File.Exists(dll), "Build the existing bounded resource probe before these controls.");
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(dll!);
        info.Environment["MALIEV_CNC_PROBE_MODE"] = "natural-exit";
        var child = new MemberAuthorityChild();
        Exception? primary = null;
        try
        {
            child.Start(info);
            using var borrowed = new Process();
            if (rejection == "borrowed")
            {
                Assert.Throws<InvalidOperationException>(() => child.VerifyOwnership(borrowed));
            }
            else if (rejection != "already-exited")
            {
                Assert.Throws<InvalidOperationException>(() => child.VerifyObservedIdentity(
                    rejection == "pid-reuse" ? child.Process.Id + 1 : child.Process.Id,
                    rejection == "birth" ? child.Process.StartTime.ToUniversalTime().AddTicks(1) : child.Process.StartTime.ToUniversalTime(),
                    rejection == "executable" ? "not-the-owned-executable" : MemberAuthorityChild.ReadExecutableIdentity(child.Process)));
            }

            Assert.False(child.Process.HasExited);
            await child.Process.StandardInput.WriteLineAsync("release-owned-natural-exit");
            await child.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, child.Process.ExitCode);
        }
        catch (Exception exception)
        {
            primary = exception;
        }

        await MemberAuthorityCleanup.CompleteAsync(primary, [() => child.DisposeAsync().AsTask()]);
        Assert.True(child.IsClosed);
        Assert.True(child.ReadersSettled);
        await child.DisposeAsync();
    }

    /// <summary>A failure immediately after process creation still releases that exact child and keeps the primary cause.</summary>
    /// <returns>A task completing the real partial-start cleanup assertion.</returns>
    [Fact]
    public async Task AfterStartFailure_ReleasesRegisteredChildWithoutReplacingPrimary()
    {
        var dll = Environment.GetEnvironmentVariable("MALIEV_CNC_LEASE_PROBE_DLL");
        Assert.True(File.Exists(dll));
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(dll!);
        info.Environment["MALIEV_CNC_PROBE_MODE"] = "partial-start";
        var child = new MemberAuthorityChild();
        var primary = new InvalidOperationException("controlled-after-start");
        var failure = Record.Exception(() => child.Start(info, () => throw primary));
        var completed = await Record.ExceptionAsync(() => MemberAuthorityCleanup.CompleteAsync(
            failure, [() => child.DisposeAsync().AsTask()]));

        Assert.Same(primary, completed);
        Assert.True(child.IsClosed);
        Assert.True(child.ReadersSettled);
    }

    /// <summary>Every fallible initialization boundary retains each unverified live child separately and refuses to signal it.</summary>
    /// <param name="stageName">The actual boundary immediately before a handle, PID, reader or executable observation.</param>
    /// <returns>A task completing controlled refusal, natural-exit recovery and all-attempt cleanup.</returns>
    [Theory]
    [InlineData("Started")]
    [InlineData("HandleCaptured")]
    [InlineData("PidCaptured")]
    [InlineData("StdoutCaptured")]
    [InlineData("StderrCaptured")]
    public async Task PartialInitialization_RetainsUniqueCustodyBeforeEveryGetter(string stageName)
    {
        var dll = Environment.GetEnvironmentVariable("MALIEV_CNC_LEASE_PROBE_DLL");
        Assert.True(File.Exists(dll));
        var stage = Enum.Parse<MemberAuthorityStartStage>(stageName);
        var children = new List<MemberAuthorityChild>();
        Exception? primary = null;
        try
        {
            for (var index = 0; index < 2; index++)
            {
                var child = new MemberAuthorityChild();
                children.Add(child);
                var info = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                info.ArgumentList.Add(dll!);
                info.Environment["MALIEV_CNC_PROBE_MODE"] = "natural-exit";
                var injected = new InvalidOperationException("controlled-initialization-boundary");
                Assert.Same(injected, Record.Exception(() => child.Start(info, observeInitialization: observed =>
                {
                    if (observed == stage) throw injected;
                })));
                Assert.True(child.Expires > DateTimeOffset.UtcNow);
                Assert.NotNull(await Record.ExceptionAsync(() => child.DisposeAsync().AsTask()));
                Assert.False(child.IsClosed);
                Assert.False(child.Process.HasExited);
                Assert.True(child.ReadersSettled);
                Assert.Same(child, MemberAuthorityChild.RetainedCustody[child.CreationToken]);
            }

            Assert.NotEqual(children[0].CreationToken, children[1].CreationToken);
            Assert.Same(children[0], MemberAuthorityChild.RetainedCustody[children[0].CreationToken]);
            Assert.Same(children[1], MemberAuthorityChild.RetainedCustody[children[1].CreationToken]);
        }
        catch (Exception exception) { primary = exception; }

        await MemberAuthorityCleanup.CompleteAsync(primary, children.Select<MemberAuthorityChild, Func<Task>>(child =>
            () => MemberAuthorityCleanup.CompleteAsync(null,
            [
                async () =>
                {
                    if (child.WasStarted && !child.Process.HasExited)
                    {
                        await child.Process.StandardInput.WriteLineAsync("release-owned-natural-exit");
                        await child.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    }
                },
                () => child.DisposeAsync().AsTask(),
            ])));
        Assert.All(children, child => Assert.True(child.IsClosed));
        Assert.All(children, child => Assert.False(MemberAuthorityChild.RetainedCustody.ContainsKey(child.CreationToken)));
    }

    /// <summary>Linux executable observation refuses foreign PID namespaces and nonabsolute executable links.</summary>
    /// <param name="mutation">The controlled namespace or executable-link mismatch.</param>
    [Theory]
    [InlineData("foreign-namespace")]
    [InlineData("missing-self")]
    [InlineData("relative-executable")]
    public void ExecutableObservation_RejectsUnprovenProcIdentity(string mutation)
    {
        var paths = new List<string>();
        Assert.Throws<InvalidOperationException>(() => MemberAuthorityChild.ReadLinuxExecutableIdentity(42, path =>
        {
            paths.Add(path);
            if (path == "/proc/self")
                return mutation == "missing-self" ? null
                    : (Environment.ProcessId + (mutation == "foreign-namespace" ? 1 : 0))
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
            return "dotnet";
        }));

        Assert.Equal(mutation == "relative-executable" ? 2 : 1, paths.Count);
        Assert.Equal("/proc/self", paths[0]);
    }
}
