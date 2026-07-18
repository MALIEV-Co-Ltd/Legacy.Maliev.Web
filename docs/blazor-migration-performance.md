# Blazor migration performance evidence

This document records the no-cost local comparison used to close the Web migration. Measurements ran on the same Windows host, Release configuration, `Testing` environment, loopback HTTP, and 500 sequential requests after 100 warm-up requests. They are a repeatable review aid, not a production load forecast.

The historical Razor Pages/jQuery baseline is commit `40e2ef8769457e8ea75bb8f840aab6c8aff79091`, the last jQuery-enabled hybrid route owner and parent of the jQuery-removal change. The migrated Blazor static SSR candidate is commit `b28193fb5d0477215eb964c41b1d9dd71f18cba9`. The candidate's later documentation-only closure commit does not change its runtime binary or browser assets.

## Results

| Measure | Razor Pages/jQuery baseline | Blazor static SSR | Change |
| --- | ---: | ---: | ---: |
| P50 latency | 2.875 ms | 1.574 ms | 45.3% lower |
| P95 latency | 4.854 ms | 3.080 ms | 36.5% lower |
| Throughput | 303.13 requests/s | 520.37 requests/s | 71.7% higher |
| CPU for 500 requests | 1.891 s | 1.125 s | 40.5% lower |
| Private memory after sample | 57.81 MB | 58.67 MB | 1.5% higher |
| Working set after sample | 113.82 MB | 116.00 MB | 1.9% higher |
| Shipped JavaScript and CSS | 599,131 bytes | 507,478 bytes | 15.3% lower |

The latency, throughput, CPU, and asset results are material improvements. The 0.86 MB private-memory and 2.18 MB working-set differences are below 2% on this host and are treated as sampling variance rather than a resource regression. No Blazor runtime script is shipped to these static SSR pages, so public visitors do not create a server circuit or download a WebAssembly runtime.

## Reproduce

Build each commit in Release, start its Web DLL with `ASPNETCORE_ENVIRONMENT=Testing` on a separate loopback port, then run:

```powershell
.\scripts\measure-local-web-performance.ps1 `
  -Uri 'http://127.0.0.1:5192/?culture=en' `
  -TargetProcessId $webProcess.Id `
  -AssetDirectory '.\Legacy.Maliev.Web\wwwroot\dist'
```

The script fails on non-success HTTP responses and emits timestamped JSON containing request counts, P50/P95/maximum latency, throughput, CPU delta, process private/working memory, and JavaScript/CSS bytes. Run both samples when the machine is otherwise idle; compare multiple samples before treating sub-5% memory differences as significant.
