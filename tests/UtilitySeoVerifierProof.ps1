#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'ProductionSeoVerifier.psm1') -Force
$passed = 0
function Assert-Proof([bool] $Condition, [string] $Name) {
    if (!$Condition) { throw "Utility SEO verifier proof failed: $Name" }
    $script:passed++
}

$meta = '<html><head><meta name="robots" content="noindex,follow" /></head></html>'
Assert-Proof (Test-AccountNoIndexContract -StatusCode 200 -XRobotsTag 'noindex, follow' -Html $meta).Passed 'account positive'
Assert-Proof (!(Test-AccountNoIndexContract -StatusCode 200 -XRobotsTag '' -Html $meta).Passed) 'missing header'
Assert-Proof (!(Test-AccountNoIndexContract -StatusCode 200 -XRobotsTag 'noindex, follow' -Html ($meta + $meta)).Passed) 'duplicate metadata'
Assert-Proof (!(Test-AccountNoIndexContract -StatusCode 200 -XRobotsTag 'noindex, follow' -Html $meta.Replace('noindex,follow', 'noindex,nofollow')).Passed) 'login checker does not redefine credential policy'

$consent = @'
<script>var denied='denied';gtag('consent','default',{'analytics_storage':denied,'ad_storage':denied});</script>
<script>owned-container-marker</script><a href="https://line.me/owned-fixture">Contact</a>
'@
Assert-Proof (Test-HomepageMeasurementContract -Html $consent -ExpectedContainer 'owned-container-marker' -LegacyIdentifiers @('unwanted-marker')).Passed 'denied variable before container'
Assert-Proof (!(Test-HomepageMeasurementContract -Html ($consent + 'unwanted-marker') -ExpectedContainer 'owned-container-marker' -LegacyIdentifiers @('unwanted-marker')).Passed) 'legacy marker rejection'

# Execute the actual CLI wrapper's failed-audit branch with an injected, entirely offline transport.
$wrapperFailed = $false
try {
    & (Join-Path $PSScriptRoot 'VerifyProductionSeo.ps1') -HttpUri 'http://127.0.0.1/' -BaseUri 'http://127.0.0.1' -Request {
        param([string] $Uri, [bool] $FollowRedirects)
        [pscustomobject]@{ StatusCode = 404; Headers = @{}; Content = '<html><head></head><body>Owned missing-route fixture</body></html>' }
    } | Out-Null
}
catch { $wrapperFailed = $_.Exception.Message.StartsWith('Production SEO verification failed:', [StringComparison]::Ordinal) }
Assert-Proof $wrapperFailed 'actual wrapper rejects failed offline audit'

# The actual exported transport runs only against this owned ephemeral loopback server.
Add-Type -TypeDefinition @'
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
public sealed class UtilitySeoLoopback : IDisposable
{
    readonly HttpListener listener = new HttpListener();
    readonly Task serving;
    public string Origin { get; }
    public UtilitySeoLoopback()
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        Origin = "http://127.0.0.1:" + port;
        listener.Prefixes.Add(Origin + "/");
        listener.Start();
        serving = Serve();
    }
    async Task Serve()
    {
        try
        {
            while (listener.IsListening)
            {
                var request = await listener.GetContextAsync();
                if (request.Request.Url.AbsolutePath == "/redirect")
                {
                    request.Response.StatusCode = 308;
                    request.Response.Headers["Location"] = Origin + "/final";
                }
                else if (request.Request.Url.AbsolutePath == "/final")
                {
                    request.Response.StatusCode = 200;
                    request.Response.ContentType = "text/plain; charset=utf-8";
                    var bytes = Encoding.UTF8.GetBytes("owned-loopback-final");
                    request.Response.ContentLength64 = bytes.Length;
                    await request.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                }
                else request.Response.StatusCode = 404;
                request.Response.Close();
            }
        }
        catch (HttpListenerException) when (!listener.IsListening) { }
        catch (ObjectDisposedException) when (!listener.IsListening) { }
    }
    public void Dispose()
    {
        listener.Stop();
        listener.Close();
        if (!serving.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Owned loopback did not stop.");
    }
}
'@
$server = [UtilitySeoLoopback]::new()
try {
    $redirect = Invoke-ProductionSeoHttpRequest -Uri ($server.Origin + '/redirect') -FollowRedirects $false
    Assert-Proof ([int]$redirect.StatusCode -eq 308 -and [string]$redirect.Headers.Location -ceq ($server.Origin + '/final')) 'actual no-follow transport'
    $followed = Invoke-ProductionSeoHttpRequest -Uri ($server.Origin + '/redirect') -FollowRedirects $true
    Assert-Proof ([int]$followed.StatusCode -eq 200 -and [string]$followed.Content -ceq 'owned-loopback-final') 'actual followed transport'
}
finally { $server.Dispose() }
[pscustomobject]@{ schemaVersion = 1; passed = $passed; failed = 0; transport = 'owned-loopback-only'; powerShellVersion = $PSVersionTable.PSVersion.ToString() } | ConvertTo-Json -Compress
