$ErrorActionPreference = 'Stop'

$modulePath = Join-Path $PSScriptRoot 'ProductionSeoVerifier.psm1'
if (Test-Path -LiteralPath $modulePath) {
    Import-Module $modulePath -Force
}

function Invoke-ContractCheck {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [hashtable]$Arguments
    )

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return [pscustomobject]@{
            Passed = $false
            Actual = "Missing verifier command: $Name"
        }
    }

    return & $command @Arguments
}

$expectedHsts = 'max-age=63072000; includeSubDomains'
$securityHeaderPolicyPath = Join-Path $PSScriptRoot '..\Legacy.Maliev.Web\Middleware\WebContentSecurityPolicyMiddleware.cs'
$expectedPaths = @(
    '/',
    '/services',
    '/services/custom-manufacturing',
    '/services/3d-design',
    '/services/silicone-casting',
    '/services/low-volume-injection-molding',
    '/services/cnc-machining',
    '/services/3d-printing',
    '/services/3d-scanning',
    '/services/finishing-and-color',
    '/about',
    '/about/socialmedia',
    '/contact',
    '/career',
    '/instantquotation/3d-printing',
    '/knowledges',
    '/knowledges/guidelines',
    '/knowledges/workflow',
    '/knowledges/specifications',
    '/knowledges/specifications/cnc-machining',
    '/knowledges/specifications/3d-printing',
    '/knowledges/specifications/3d-scanning',
    '/legal',
    '/legal/privacypolicy',
    '/legal/termsconditions',
    '/legal/nondisclosureagreement'
)

$validHomepage = @'
<html lang="th"><head>
<title>รับผลิตชิ้นส่วนตามแบบ | MALIEV</title>
<meta name="description" content="รับผลิตชิ้นส่วนตามแบบด้วย CNC พิมพ์ 3D สแกน 3D และออกแบบ 3D ในนนทบุรีและกรุงเทพ" />
<meta name="robots" content="index,follow" />
<meta property="og:title" content="รับผลิตชิ้นส่วนตามแบบ | MALIEV" />
<meta property="og:description" content="รับผลิตชิ้นส่วนตามแบบด้วย CNC และ 3D printing" />
<meta property="og:url" content="https://www.maliev.com/" />
<meta property="og:image" content="https://www.maliev.com/src/images/landing/landing-hero-cnc.webp" />
<link rel="canonical" href="https://www.maliev.com/" />
<link rel="alternate" href="https://www.maliev.com/?culture=en" hreflang="en" />
<link rel="alternate" href="https://www.maliev.com/" hreflang="th" />
<link rel="alternate" href="https://www.maliev.com/" hreflang="x-default" />
<script>window.dataLayer=window.dataLayer||[];window.gtag=function(){dataLayer.push(arguments);};window.gtag('consent','default',{'analytics_storage':'denied','ad_storage':'denied'});</script>
<script>(function(w,d,s,l,i){})(window,document,'script','dataLayer','GTM-KHDDLVRR');</script>
<script type="application/ld+json">
{
  "@context": "https://schema.org",
  "@type": "LocalBusiness",
  "openingHoursSpecification": [{
    "@type": "OpeningHoursSpecification",
    "dayOfWeek": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
    "opens": "09:00",
    "closes": "18:00"
  }]
}
</script>
</head><body><main><h1>Manufacturing Services for Prototypes and Production Parts</h1></main><a href="https://line.me/ti/p/@maliev">LINE</a></body></html>
'@
$validRouteMeasurement = @'
<html lang="th"><head>
<title>Public manufacturing page | MALIEV</title>
<meta name="description" content="MALIEV provides manufacturing services and project review for customers in Thailand and beyond." />
<meta name="robots" content="index,follow" />
<meta property="og:title" content="Public manufacturing page | MALIEV" />
<meta property="og:description" content="Review manufacturing services and project requirements with MALIEV." />
<meta property="og:url" content="__CANONICAL__" />
<meta property="og:image" content="https://www.maliev.com/src/images/landing/landing-hero-cnc.webp" />
<link rel="canonical" href="__CANONICAL__" />
<link rel="alternate" href="__EN__" hreflang="en" />
<link rel="alternate" href="__CANONICAL__" hreflang="th" />
<link rel="alternate" href="__CANONICAL__" hreflang="x-default" />
<script>window.dataLayer=window.dataLayer||[];window.gtag=window.gtag||function(){dataLayer.push(arguments);};window.gtag('consent','default',{'analytics_storage':'denied','ad_storage':'denied'});</script>
<script>(function(w,d,s,l,i){w[l]=w[l]||[];w[l].push({'gtm.start':new Date().getTime(),event:'gtm.js'});var f=d.getElementsByTagName(s)[0],j=d.createElement(s),dl=l!='dataLayer'?'&l='+l:'';j.async=true;j.src='https://www.googletagmanager.com/gtm.js?id='+i+dl;f.parentNode.insertBefore(j,f);})(window,document,'script','dataLayer','GTM-KHDDLVRR');</script>
<script>window.dataLayer.push({event:'maliev_contact_click',channel:'line',destination:'line_oa',context:'other'});</script>
<script>window.dataLayer.push({event:'maliev_review_link_click',platform:'google_business_profile',context:'other'});</script>
<script type="application/ld+json">{"@context":"https://schema.org","@type":"Organization","url":"https://www.maliev.com"}</script>
</head><body><main><h1>Public manufacturing page</h1></main><a href="https://line.me/ti/p/@maliev">LINE</a><a data-maliev-review-platform="google_business_profile" target="_blank" href="https://maps.google.com">review</a></body></html>
'@

$validPublicSeoPage = @'
<html lang="th"><head>
<title>รับพิมพ์ 3D กรุงเทพและนนทบุรี | MALIEV</title>
<meta name="description" content="MALIEV รับพิมพ์ 3D ด้วยระบบ FDM และเรซิ่นสำหรับต้นแบบและชิ้นงานใช้งานจริง เลือกวัสดุ อัปโหลดไฟล์ และประเมินราคาออนไลน์" />
<meta name="robots" content="index,follow" />
<meta property="og:title" content="รับพิมพ์ 3D กรุงเทพและนนทบุรี | MALIEV" />
<meta property="og:description" content="MALIEV รับพิมพ์ 3D สำหรับต้นแบบและชิ้นงานใช้งานจริง" />
<meta property="og:url" content="https://www.maliev.com/services/3d-printing" />
<meta property="og:image" content="https://www.maliev.com/src/images/landing/landing-hero-cnc.webp" />
<link rel="canonical" href="https://www.maliev.com/services/3d-printing" />
<link rel="alternate" href="https://www.maliev.com/services/3d-printing?culture=en" hreflang="en" />
<link rel="alternate" href="https://www.maliev.com/services/3d-printing" hreflang="th" />
<link rel="alternate" href="https://www.maliev.com/services/3d-printing" hreflang="x-default" />
<script type="application/ld+json">{"@context":"https://schema.org","@type":"Organization","url":"https://www.maliev.com"}</script>
</head><body><main><h1>รับพิมพ์ 3D และรับปริ้น 3D สำหรับต้นแบบและชิ้นงานใช้งานจริง</h1></main></body></html>
'@

$validSitemapUrls = $expectedPaths | ForEach-Object {
    $suffix = if ($_ -eq '/') { '/' } else { $_ }
    "<url><loc>https://www.maliev.com$suffix</loc><changefreq>monthly</changefreq><priority>0.8</priority></url>"
}
$validSitemap = '<?xml version="1.0" encoding="utf-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">' + ($validSitemapUrls -join '') + '</urlset>'

Describe 'Production SEO release contracts' {
    It 'publishes the exact expected public search route inventory' {
        $command = Get-Command 'Get-ExpectedPublicSearchPaths' -ErrorAction SilentlyContinue
        $actual = if ($null -eq $command) { @() } else { @(& $command) }

        ($actual -join '|') | Should Be ($expectedPaths -join '|')
    }

    It 'accepts the exact permanent HTTP to HTTPS redirect' {
        $result = Invoke-ContractCheck 'Test-HttpRedirectContract' @{
            StatusCode = 308
            Location = 'https://www.maliev.com/'
            ExpectedLocation = 'https://www.maliev.com/'
        }

        $result.Passed | Should Be $true
    }

    It 'rejects an HTTP response that still serves content' {
        $result = Invoke-ContractCheck 'Test-HttpRedirectContract' @{
            StatusCode = 200
            Location = ''
            ExpectedLocation = 'https://www.maliev.com/'
        }

        $result.Passed | Should Be $false
    }

    It 'accepts the exact production HSTS policy' {
        $result = Invoke-ContractCheck 'Test-HstsContract' @{
            StatusCode = 200
            Hsts = $expectedHsts
            ExpectedHsts = $expectedHsts
        }

        $result.Passed | Should Be $true
    }

    It 'allows the Google Ads conversion collection endpoint in the CSP' {
        $securityPolicy = Get-Content -LiteralPath $securityHeaderPolicyPath -Raw

        $securityPolicy | Should Match 'connect-src[^\r\n]*https://ad\.doubleclick\.net'
        $securityPolicy | Should Match 'script-src[^\r\n]*https://googleads\.g\.doubleclick\.net'
        $securityPolicy | Should Match 'img-src[^\r\n]*https://googleads\.g\.doubleclick\.net'
        $securityPolicy | Should Match 'img-src[^\r\n]*https://www\.google\.com'
        $securityPolicy | Should Match 'img-src[^\r\n]*https://www\.google\.co\.th'
    }

    It 'allows Cloudflare Web Analytics to load without weakening the application policy' {
        $securityPolicy = Get-Content -LiteralPath $securityHeaderPolicyPath -Raw

        $securityPolicy | Should Match 'script-src[^\r\n]*https://static\.cloudflareinsights\.com'
        $securityPolicy | Should Match 'connect-src[^\r\n]*https://cloudflareinsights\.com'
    }

    It 'allows the Instant Quotation WebAssembly runtime without broad unsafe eval' {
        $securityPolicy = Get-Content -LiteralPath $securityHeaderPolicyPath -Raw

        $securityPolicy | Should Match "script-src[^\r\n]*'wasm-unsafe-eval'"
        $securityPolicy | Should Not Match "script-src[^\r\n]*'unsafe-eval'"
    }

    It 'accepts consent-first clean GTM markup and HTTPS LINE' {
        $result = Invoke-ContractCheck 'Test-HomepageMeasurementContract' @{
            Html = $validHomepage
            ExpectedContainer = 'GTM-KHDDLVRR'
            LegacyIdentifiers = @('GTM-5VBH5LK', 'GTM-P2KSC6C', 'UA-133315708-1')
        }

        $result.Passed | Should Be $true
    }

    It 'accepts a denied consent variable used by both storage fields before GTM' {
        $html = @'
<script>
var consentState = 'denied';
window.gtag('consent', 'default', {
  'ad_storage': consentState,
  'analytics_storage': consentState
});
</script>
<script>/* GTM-KHDDLVRR */</script>
<a href="https://line.me/ti/p/@maliev">LINE</a>
'@

        $result = Invoke-ContractCheck 'Test-HomepageMeasurementContract' @{
            Html = $html
            ExpectedContainer = 'GTM-KHDDLVRR'
            LegacyIdentifiers = @('GTM-5VBH5LK', 'UA-133315708-1')
        }

        $result.Passed | Should Be $true
    }

    It 'rejects a legacy measurement identifier' {
        $result = Invoke-ContractCheck 'Test-HomepageMeasurementContract' @{
            Html = $validHomepage + '<script>GTM-5VBH5LK</script>'
            ExpectedContainer = 'GTM-KHDDLVRR'
            LegacyIdentifiers = @('GTM-5VBH5LK', 'GTM-P2KSC6C', 'UA-133315708-1')
        }

        $result.Passed | Should Be $false
    }

    It 'accepts the complete canonical sitemap inventory' {
        $result = Invoke-ContractCheck 'Test-SitemapContract' @{
            Xml = $validSitemap
            ExpectedPaths = $expectedPaths
            CanonicalOrigin = 'https://www.maliev.com'
        }

        $result.Passed | Should Be $true
    }

    It 'rejects fabricated sitemap modification dates' {
        $result = Invoke-ContractCheck 'Test-SitemapContract' @{
            Xml = $validSitemap.Replace('</url>', '<lastmod>2569-07-13</lastmod></url>')
            ExpectedPaths = $expectedPaths
            CanonicalOrigin = 'https://www.maliev.com'
        }

        $result.Passed | Should Be $false
    }

    It 'accepts a page whose canonical URL matches the requested URL' {
        $result = Invoke-ContractCheck 'Test-CanonicalContract' @{
            PageUri = 'https://www.maliev.com/services/3d-printing'
            Html = '<html><head><link rel="canonical" href="https://www.maliev.com/services/3d-printing" /></head></html>'
        }

        $result.Passed | Should Be $true
    }

    It 'accepts the English culture cookie redirect to the explicit localized URL' {
        $result = Invoke-ContractCheck 'Test-EnglishCookieCanonicalRedirectContract' @{
            StatusCode = 301
            Location = 'https://www.maliev.com/services/3d-printing?culture=en'
            ExpectedLocation = 'https://www.maliev.com/services/3d-printing?culture=en'
        }

        $result.Passed | Should Be $true
    }

    It 'accepts a complete public rendered SEO document' {
        $result = Invoke-ContractCheck 'Test-PublicPageSeoContract' @{
            PageUri = 'https://www.maliev.com/services/3d-printing'
            Html = $validPublicSeoPage
            CanonicalOrigin = 'https://www.maliev.com'
            JsonLdRequired = $true
            OpenGraphUrlRequired = $true
            OpenGraphImageRequired = $true
        }

        $result.Passed | Should Be $true
    }

    It 'ignores document markup embedded in ordinary script source' {
        $html = $validPublicSeoPage.Replace(
            '</body>',
            '<script>var printDocument = "<html><head><title>Printable quote</title></head></html>";</script></body>')
        $result = Invoke-ContractCheck 'Test-PublicPageSeoContract' @{
            PageUri = 'https://www.maliev.com/services/3d-printing'
            Html = $html
            CanonicalOrigin = 'https://www.maliev.com'
            JsonLdRequired = $true
            OpenGraphUrlRequired = $true
            OpenGraphImageRequired = $true
        }

        $result.Passed | Should Be $true
    }

    It 'rejects a public rendered SEO document without a description' {
        $result = Invoke-ContractCheck 'Test-PublicPageSeoContract' @{
            PageUri = 'https://www.maliev.com/services/3d-printing'
            Html = $validPublicSeoPage -replace '<meta name="description"[^>]+>', ''
            CanonicalOrigin = 'https://www.maliev.com'
            JsonLdRequired = $true
            OpenGraphUrlRequired = $true
            OpenGraphImageRequired = $true
        }

        $result.Passed | Should Be $false
    }

    It 'rejects a public rendered SEO document marked noindex' {
        $result = Invoke-ContractCheck 'Test-PublicPageSeoContract' @{
            PageUri = 'https://www.maliev.com/services/3d-printing'
            Html = $validPublicSeoPage -replace 'content="index,follow"', 'content="noindex,follow"'
            CanonicalOrigin = 'https://www.maliev.com'
            JsonLdRequired = $true
            OpenGraphUrlRequired = $true
            OpenGraphImageRequired = $true
        }

        $result.Passed | Should Be $false
    }

    It 'rejects duplicate canonical links on a public rendered document' {
        $result = Invoke-ContractCheck 'Test-PublicPageSeoContract' @{
            PageUri = 'https://www.maliev.com/services/3d-printing'
            Html = $validPublicSeoPage.Replace(
                '</head>',
                '<link rel="canonical" href="https://www.maliev.com/services/3d-printing" /></head>')
            CanonicalOrigin = 'https://www.maliev.com'
            JsonLdRequired = $true
            OpenGraphUrlRequired = $true
            OpenGraphImageRequired = $true
        }

        $result.Passed | Should Be $false
    }

    It 'rejects malformed structured data on a public rendered document' {
        $result = Invoke-ContractCheck 'Test-PublicPageSeoContract' @{
            PageUri = 'https://www.maliev.com/services/3d-printing'
            Html = $validPublicSeoPage.Replace(
                '{"@context":"https://schema.org","@type":"Organization","url":"https://www.maliev.com"}',
                '{not-valid-json}')
            CanonicalOrigin = 'https://www.maliev.com'
            JsonLdRequired = $true
            OpenGraphUrlRequired = $true
            OpenGraphImageRequired = $true
        }

        $result.Passed | Should Be $false
    }

    It 'accepts weekday 09:00 to 18:00 LocalBusiness hours' {
        $result = Invoke-ContractCheck 'Test-LocalBusinessHoursContract' @{
            Html = $validHomepage
        }

        $result.Passed | Should Be $true
    }

    It 'rejects conflicting LocalBusiness hours' {
        $result = Invoke-ContractCheck 'Test-LocalBusinessHoursContract' @{
            Html = $validHomepage.Replace('09:00', '10:00').Replace('18:00', '19:00')
        }

        $result.Passed | Should Be $false
    }

    It 'accepts a personalized page that is not publicly cacheable' {
        $result = Invoke-ContractCheck 'Test-PrivateCacheContract' @{
            CacheControl = 'no-store, no-cache'
        }

        $result.Passed | Should Be $true
    }

    It 'rejects public caching on a personalized page' {
        $result = Invoke-ContractCheck 'Test-PrivateCacheContract' @{
            CacheControl = 'public, max-age=3600'
        }

        $result.Passed | Should Be $false
    }

    It 'accepts an account utility page that is noindex follow in headers and HTML' {
        $result = Invoke-ContractCheck 'Test-AccountNoIndexContract' @{
            StatusCode = 200
            XRobotsTag = 'noindex, follow'
            Html = '<html><head><meta name="robots" content="noindex,follow" /></head></html>'
        }

        $result.Passed | Should Be $true
    }

    It 'rejects an account utility page without the noindex response header' {
        $result = Invoke-ContractCheck 'Test-AccountNoIndexContract' @{
            StatusCode = 200
            XRobotsTag = ''
            Html = '<html><head><meta name="robots" content="noindex,follow" /></head></html>'
        }

        $result.Passed | Should Be $false
    }

    It 'rejects duplicate robots meta directives on an account utility page' {
        $result = Invoke-ContractCheck 'Test-AccountNoIndexContract' @{
            StatusCode = 200
            XRobotsTag = 'noindex, follow'
            Html = '<html><head><meta name="robots" content="noindex,follow" /><meta name="robots" content="noindex,follow" /></head></html>'
        }

        $result.Passed | Should Be $false
    }

    It 'returns a no-follow redirect response when Invoke-WebRequest throws for the redirect' {
        Mock Invoke-WebRequest {
            $response = [System.Net.Http.HttpResponseMessage]::new(
                [System.Net.HttpStatusCode]::PermanentRedirect)
            $response.Headers.Location = [uri]'https://www.maliev.com/'
            throw [Microsoft.PowerShell.Commands.HttpResponseException]::new(
                'Maximum redirection count exceeded.',
                $response)
        } -ModuleName ProductionSeoVerifier

        $command = Get-Command 'Invoke-ProductionSeoHttpRequest' -ErrorAction SilentlyContinue
        $result = if ($null -eq $command) {
            $null
        }
        else {
            & $command -Uri 'http://www.maliev.com/' -FollowRedirects $false
        }

        ($null -ne $result) | Should Be $true
        [int]$result.StatusCode | Should Be 308
        [string]$result.Headers.Location | Should Be 'https://www.maliev.com/'
    }

    It 'suppresses PowerShells non-terminating maximum-redirection error for no-follow requests' {
        Mock Invoke-WebRequest {
            Write-Error 'Maximum redirection count exceeded.'
            return [pscustomobject]@{
                StatusCode = 308
                Headers = @{ Location = 'https://www.maliev.com/' }
                Content = ''
            }
        } -ModuleName ProductionSeoVerifier

        $result = Invoke-ProductionSeoHttpRequest `
            -Uri 'http://www.maliev.com/' `
            -FollowRedirects $false

        [int]$result.StatusCode | Should Be 308
    }

    It 'orchestrates every public release check through an injected HTTP transport' {
        $canonicalOrigin = 'https://www.maliev.com'
        $homepageFixture = $validHomepage
        $routeMeasurementFixture = $validRouteMeasurement
        $sitemapFixture = $validSitemap
        $hstsFixture = $expectedHsts
        $request = {
            param(
                [string]$Uri,
                [bool]$FollowRedirects
            )

            if ($Uri -eq 'http://www.maliev.com/') {
                return [pscustomobject]@{
                    StatusCode = 308
                    Headers = @{ Location = "$canonicalOrigin/" }
                    Content = ''
                }
            }

            if ($Uri -eq "$canonicalOrigin/") {
                return [pscustomobject]@{
                    StatusCode = 200
                    Headers = @{ 'Strict-Transport-Security' = $hstsFixture }
                    Content = $homepageFixture
                }
            }

            if ($Uri -eq "$canonicalOrigin/robots.txt") {
                return [pscustomobject]@{
                    StatusCode = 200
                    Headers = @{}
                    Content = "User-agent: *`nAllow: /`nSitemap: $canonicalOrigin/sitemap"
                }
            }

            if ($Uri -eq "$canonicalOrigin/sitemap") {
                return [pscustomobject]@{
                    StatusCode = 200
                    Headers = @{ 'Content-Type' = 'application/xml' }
                    Content = $sitemapFixture
                }
            }

            if ($Uri -eq "$canonicalOrigin/account/login") {
                return [pscustomobject]@{
                    StatusCode = 200
                    Headers = @{ 'X-Robots-Tag' = 'noindex, follow' }
                    Content = '<html><head><meta name="robots" content="noindex,follow" /></head></html>'
                }
            }

            $path = ([uri]$Uri).AbsolutePath
            $canonical = if ($path -eq '/') { "$canonicalOrigin/" } else { "$canonicalOrigin$path" }
            $cacheControl = if ($path -in @('/contact', '/quotation')) { 'no-store, no-cache' } else { '' }
            return [pscustomobject]@{
                StatusCode = 200
                Headers = @{ 'Cache-Control' = $cacheControl }
                Content = $routeMeasurementFixture.Replace('__CANONICAL__', $canonical).Replace('__EN__', "${canonical}?culture=en")
            }
        }.GetNewClosure()

        $command = Get-Command 'Invoke-ProductionSeoVerification' -ErrorAction SilentlyContinue
        $result = if ($null -eq $command) {
            [pscustomobject]@{ Passed = $false; Checks = @() }
        }
        else {
            & $command `
                -HttpUri 'http://www.maliev.com/' `
                -BaseUri $canonicalOrigin `
                -ExpectedPaths $expectedPaths `
                -Request $request
        }

        $result.Passed | Should Be $true
        @($result.Checks | Where-Object { -not $_.Passed }).Count | Should Be 0
        @($result.Checks | Where-Object Name -eq 'canonical_url').Count | Should Be $expectedPaths.Count
        @($result.Checks | Where-Object Name -eq 'public_page_seo').Count | Should Be $expectedPaths.Count
        @($result.Checks | Where-Object Name -eq 'account_utility_noindex').Count | Should Be 1
    }

    It 'fails the release when any orchestrated check fails' {
        $request = {
            param(
                [string]$Uri,
                [bool]$FollowRedirects
            )

            return [pscustomobject]@{
                StatusCode = 200
                Headers = @{}
                Content = '<html></html>'
            }
        }

        $command = Get-Command 'Invoke-ProductionSeoVerification' -ErrorAction SilentlyContinue
        $result = if ($null -eq $command) {
            [pscustomobject]@{ Passed = $false; Checks = @() }
        }
        else {
            & $command `
                -HttpUri 'http://www.maliev.com/' `
                -BaseUri 'https://www.maliev.com' `
                -ExpectedPaths $expectedPaths `
                -Request $request
        }

        $result.Passed | Should Be $false
        @($result.Checks | Where-Object Name -eq 'http_permanent_redirect').Count | Should Be 1
    }

    It 'runs the CLI and writes a machine-readable audit artifact' {
        $canonicalOrigin = 'https://www.maliev.com'
        $homepageFixture = $validHomepage
        $routeMeasurementFixture = $validRouteMeasurement
        $sitemapFixture = $validSitemap
        $hstsFixture = $expectedHsts
        $request = {
            param(
                [string]$Uri,
                [bool]$FollowRedirects
            )

            if ($Uri -eq 'http://www.maliev.com/') {
                return [pscustomobject]@{
                    StatusCode = 308
                    Headers = @{ Location = "$canonicalOrigin/" }
                    Content = ''
                }
            }

            if ($Uri -eq "$canonicalOrigin/") {
                return [pscustomobject]@{
                    StatusCode = 200
                    Headers = @{ 'Strict-Transport-Security' = $hstsFixture }
                    Content = $homepageFixture
                }
            }

            if ($Uri -eq "$canonicalOrigin/robots.txt") {
                return [pscustomobject]@{
                    StatusCode = 200
                    Headers = @{}
                    Content = "Sitemap: $canonicalOrigin/sitemap"
                }
            }

            if ($Uri -eq "$canonicalOrigin/sitemap") {
                return [pscustomobject]@{
                    StatusCode = 200
                    Headers = @{ 'Content-Type' = 'application/xml' }
                    Content = $sitemapFixture
                }
            }

            if ($Uri -eq "$canonicalOrigin/account/login") {
                return [pscustomobject]@{
                    StatusCode = 200
                    Headers = @{ 'X-Robots-Tag' = 'noindex, follow' }
                    Content = '<html><head><meta name="robots" content="noindex,follow" /></head></html>'
                }
            }

            $path = ([uri]$Uri).AbsolutePath
            $canonical = if ($path -eq '/') { "$canonicalOrigin/" } else { "$canonicalOrigin$path" }
            $cacheControl = if ($path -in @('/contact', '/quotation')) { 'no-store' } else { '' }
            return [pscustomobject]@{
                StatusCode = 200
                Headers = @{ 'Cache-Control' = $cacheControl }
                Content = $routeMeasurementFixture.Replace('__CANONICAL__', $canonical).Replace('__EN__', "${canonical}?culture=en")
            }
        }.GetNewClosure()

        $runnerPath = Join-Path $PSScriptRoot 'VerifyProductionSeo.ps1'
        $auditPath = Join-Path $TestDrive 'production-seo-audit.json'
        $result = if (Test-Path -LiteralPath $runnerPath) {
            & $runnerPath `
                -HttpUri 'http://www.maliev.com/' `
                -BaseUri $canonicalOrigin `
                -OutputPath $auditPath `
                -Request $request
        }
        else {
            $null
        }

        ($null -ne $result) | Should Be $true
        (Test-Path -LiteralPath $auditPath) | Should Be $true
        if (Test-Path -LiteralPath $auditPath) {
            $audit = Get-Content -Raw $auditPath | ConvertFrom-Json
            $audit.Passed | Should Be $true
            @($audit.Checks).Count | Should BeGreaterThan 20
        }
    }
}
