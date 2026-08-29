$ErrorActionPreference = 'Stop'

function Get-ExpectedPublicSearchPaths {
    return @(
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
        '/quotation',
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
}

function New-ContractResult {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [bool]$Passed,

        [Parameter(Mandatory = $true)]
        [string]$Expected,

        [AllowEmptyString()]
        [string]$Actual = ''
    )

    return [pscustomobject]@{
        Name = $Name
        Passed = $Passed
        Expected = $Expected
        Actual = $Actual
    }
}

function Test-HttpRedirectContract {
    param(
        [Parameter(Mandatory = $true)]
        [int]$StatusCode,

        [AllowEmptyString()]
        [string]$Location = '',

        [Parameter(Mandatory = $true)]
        [string]$ExpectedLocation
    )

    $passed = $StatusCode -eq 308 -and $Location -ceq $ExpectedLocation
    return New-ContractResult `
        -Name 'http_permanent_redirect' `
        -Passed $passed `
        -Expected "308 Location: $ExpectedLocation" `
        -Actual "$StatusCode Location: $Location"
}

function Test-HstsContract {
    param(
        [Parameter(Mandatory = $true)]
        [int]$StatusCode,

        [AllowEmptyString()]
        [string]$Hsts = '',

        [Parameter(Mandatory = $true)]
        [string]$ExpectedHsts
    )

    $passed = $StatusCode -eq 200 -and $Hsts -ceq $ExpectedHsts
    return New-ContractResult `
        -Name 'https_hsts' `
        -Passed $passed `
        -Expected "200 Strict-Transport-Security: $ExpectedHsts" `
        -Actual "$StatusCode Strict-Transport-Security: $Hsts"
}

function Test-HomepageMeasurementContract {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Html,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedContainer,

        [Parameter(Mandatory = $true)]
        [string[]]$LegacyIdentifiers
    )

    $containerIndex = $Html.IndexOf($ExpectedContainer, [StringComparison]::Ordinal)
    $consentIndex = $Html.IndexOf("gtag('consent','default'", [StringComparison]::OrdinalIgnoreCase)
    if ($consentIndex -lt 0) {
        $consentIndex = $Html.IndexOf("gtag('consent', 'default'", [StringComparison]::OrdinalIgnoreCase)
    }

    $analyticsDenied = $Html -match '(?is)[''"]analytics_storage[''"]\s*:\s*[''"]denied[''"]'
    $adsDenied = $Html -match '(?is)[''"]ad_storage[''"]\s*:\s*[''"]denied[''"]'
    $deniedVariable = [regex]::Match(
        $Html,
        '(?is)\b(?:var|let|const)\s+([A-Za-z_$][A-Za-z0-9_$]*)\s*=\s*[''"]denied[''"]')
    if ($deniedVariable.Success -and $deniedVariable.Index -lt $consentIndex) {
        $variableName = [regex]::Escape($deniedVariable.Groups[1].Value)
        $analyticsVariablePattern = '(?is)[''"]analytics_storage[''"]\s*:\s*{0}\b' -f $variableName
        $adsVariablePattern = '(?is)[''"]ad_storage[''"]\s*:\s*{0}\b' -f $variableName
        $analyticsDenied = $analyticsDenied -or ($Html -match $analyticsVariablePattern)
        $adsDenied = $adsDenied -or ($Html -match $adsVariablePattern)
    }
    $httpsLine = $Html -match 'https://line\.me/'
    $presentLegacy = @($LegacyIdentifiers | Where-Object {
        $Html.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })

    $passed = $containerIndex -ge 0 `
        -and $consentIndex -ge 0 `
        -and $consentIndex -lt $containerIndex `
        -and $analyticsDenied `
        -and $adsDenied `
        -and $httpsLine `
        -and $presentLegacy.Count -eq 0

    $actual = [ordered]@{
        expectedContainerPresent = $containerIndex -ge 0
        consentDefaultBeforeContainer = $consentIndex -ge 0 -and $consentIndex -lt $containerIndex
        analyticsStorageDenied = [bool]$analyticsDenied
        adStorageDenied = [bool]$adsDenied
        httpsLinePresent = [bool]$httpsLine
        legacyIdentifiersPresent = $presentLegacy
    } | ConvertTo-Json -Compress

    return New-ContractResult `
        -Name 'homepage_measurement_source' `
        -Passed $passed `
        -Expected "Consent denied before $ExpectedContainer; HTTPS LINE; no legacy identifiers" `
        -Actual $actual
}

function Test-RouteMeasurementContract {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PageUri,

        [Parameter(Mandatory = $true)]
        [string]$Html,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedContainer,

        [string[]]$LegacyIdentifiers = @('GTM-5VBH5LK', 'GTM-P2KSC6C', 'UA-133315708-1')
    )

    $containerIndex = $Html.IndexOf($ExpectedContainer, [StringComparison]::Ordinal)
    $consentIndex = $Html.IndexOf("gtag('consent','default'", [StringComparison]::OrdinalIgnoreCase)
    if ($consentIndex -lt 0) {
        $consentIndex = $Html.IndexOf("gtag('consent', 'default'", [StringComparison]::OrdinalIgnoreCase)
    }

    $analyticsDenied = $Html -match '(?is)\banalytics_storage[^\n\r<>]*denied\b'
    $adsDenied = $Html -match '(?is)\bad_storage[^\n\r<>]*denied\b'
    $deniedVariable = [regex]::Match(
        $Html,
        '(?is)\b(?:var|let|const)\s+([A-Za-z_$][A-Za-z0-9_$]*)\s*=\s*[''"]denied[''"]')
    if ($deniedVariable.Success -and $deniedVariable.Index -lt $consentIndex) {
        $variableName = [regex]::Escape($deniedVariable.Groups[1].Value)
    $analyticsVariablePattern = '(?is)["'']analytics_storage["'']\s*:\s*{0}\b' -f $variableName
    $adsVariablePattern = '(?is)["'']ad_storage["'']\s*:\s*{0}\b' -f $variableName
        $analyticsDenied = $analyticsDenied -or ($Html -match $analyticsVariablePattern)
        $adsDenied = $adsDenied -or ($Html -match $adsVariablePattern)
    }

    $hasDataLayer = $Html -match '(?is)window\.dataLayer\s*=\s*window\.dataLayer\s*\|\|\s*\[\]'
    $hasGtagShim = $Html -match '(?is)window\.gtag\s*=\s*window\.gtag\s*\|\|\s*function'
    $hasGtmLauncher = $Html.IndexOf("'https://www.googletagmanager.com/gtm.js?id='", [StringComparison]::OrdinalIgnoreCase) -ge 0
    $hasContactTracking = $Html -match '(?is)\bmaliev_contact_click\b' -and $Html -match '(?is)\bmaliev_review_link_click\b'
    $presentLegacy = @($LegacyIdentifiers | Where-Object {
        $Html.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })

    $passed = $containerIndex -ge 0 `
        -and $consentIndex -ge 0 `
        -and $consentIndex -lt $containerIndex `
        -and $analyticsDenied `
        -and $adsDenied `
        -and $hasDataLayer `
        -and $hasGtagShim `
        -and $hasGtmLauncher `
        -and $hasContactTracking `
        -and $presentLegacy.Count -eq 0

    $actual = [ordered]@{
        pageUri = $PageUri
        expectedContainerPresent = $containerIndex -ge 0
        consentDefaultBeforeContainer = $consentIndex -ge 0 -and $consentIndex -lt $containerIndex
        analyticsStorageDenied = [bool]$analyticsDenied
        adStorageDenied = [bool]$adsDenied
        dataLayerInitialized = [bool]$hasDataLayer
        gtagShimInitialized = [bool]$hasGtagShim
        gtmLauncherPresent = [bool]$hasGtmLauncher
        contactAndReviewTrackingPresent = [bool]$hasContactTracking
        legacyIdentifiersPresent = $presentLegacy
    } | ConvertTo-Json -Compress

    return New-ContractResult `
        -Name 'route_measurement_source' `
        -Passed $passed `
        -Expected "Route measurement scaffold present and consent-first with $ExpectedContainer" `
        -Actual $actual
}

function Test-SitemapContract {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Xml,

        [Parameter(Mandatory = $true)]
        [string[]]$ExpectedPaths,

        [Parameter(Mandatory = $true)]
        [string]$CanonicalOrigin
    )

    try {
        $document = New-Object System.Xml.XmlDocument
        $document.PreserveWhitespace = $true
        $document.LoadXml($Xml)
    }
    catch {
        return New-ContractResult `
            -Name 'sitemap_inventory' `
            -Passed $false `
            -Expected 'Valid XML with the complete canonical inventory and no lastmod elements' `
            -Actual "Invalid XML: $($_.Exception.Message)"
    }

    $locations = @($document.SelectNodes("//*[local-name()='loc']") | ForEach-Object {
        $_.InnerText.Trim()
    })
    $lastmodCount = $document.SelectNodes("//*[local-name()='lastmod']").Count
    $expectedLocations = @($ExpectedPaths | ForEach-Object {
        if ($_ -eq '/') {
            return "$($CanonicalOrigin.TrimEnd('/'))/"
        }

        return "$($CanonicalOrigin.TrimEnd('/'))$_"
    })

    $missing = @($expectedLocations | Where-Object { $_ -cnotin $locations })
    $unexpected = @($locations | Where-Object { $_ -cnotin $expectedLocations })
    $duplicates = @($locations | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    $passed = $lastmodCount -eq 0 `
        -and $locations.Count -eq $expectedLocations.Count `
        -and $missing.Count -eq 0 `
        -and $unexpected.Count -eq 0 `
        -and $duplicates.Count -eq 0

    $actual = [ordered]@{
        locationCount = $locations.Count
        expectedLocationCount = $expectedLocations.Count
        lastmodCount = $lastmodCount
        missing = $missing
        unexpected = $unexpected
        duplicates = $duplicates
    } | ConvertTo-Json -Compress

    return New-ContractResult `
        -Name 'sitemap_inventory' `
        -Passed $passed `
        -Expected 'Exact canonical route inventory with no duplicates or fabricated lastmod elements' `
        -Actual $actual
}

function Test-CanonicalContract {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PageUri,

        [Parameter(Mandatory = $true)]
        [string]$Html
    )

    $canonical = ''
    foreach ($tag in [regex]::Matches($Html, '(?is)<link\b[^>]*>')) {
        if ($tag.Value -notmatch '(?is)\brel\s*=\s*["'']canonical["'']') {
            continue
        }

        $href = [regex]::Match($tag.Value, '(?is)\bhref\s*=\s*["''](?<href>[^"'']+)["'']')
        if ($href.Success) {
            $canonical = $href.Groups['href'].Value
            break
        }
    }

    $expected = ([uri]$PageUri).GetLeftPart([System.UriPartial]::Path).TrimEnd('/')
    $actual = if ([string]::IsNullOrWhiteSpace($canonical)) {
        ''
    }
    else {
        ([uri]$canonical).GetLeftPart([System.UriPartial]::Path).TrimEnd('/')
    }

    return New-ContractResult `
        -Name 'canonical_url' `
        -Passed ($actual -ceq $expected) `
        -Expected $expected `
        -Actual $actual
}

function Get-HtmlAttributeValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $pattern = '(?is)\b{0}\s*=\s*["''](?<value>[^"'']*)["'']' -f [regex]::Escape($Name)
    $match = [regex]::Match($Tag, $pattern)
    if (-not $match.Success) {
        return ''
    }

    return [System.Net.WebUtility]::HtmlDecode($match.Groups['value'].Value)
}

function Test-PublicPageSeoContract {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PageUri,

        [Parameter(Mandatory = $true)]
        [string]$Html,

        [Parameter(Mandatory = $true)]
        [string]$CanonicalOrigin,

        [bool]$JsonLdRequired = $false,

        [bool]$OpenGraphUrlRequired = $false,

        [bool]$OpenGraphImageRequired = $false,

        [bool]$AllowMultipleH1 = $false
    )

    $documentHtml = [regex]::Replace(
        $Html,
        '(?is)<script\b(?![^>]*\btype\s*=\s*["'']application/ld\+json["''])[^>]*>.*?</script>',
        '')
    $metaTags = @([regex]::Matches($documentHtml, '(?is)<meta\b[^>]*>'))
    $linkTags = @([regex]::Matches($documentHtml, '(?is)<link\b[^>]*>'))
    $titleTags = @([regex]::Matches($documentHtml, '(?is)<title\b[^>]*>(?<value>.*?)</title>'))
    $h1Tags = @([regex]::Matches($documentHtml, '(?is)<h1\b[^>]*>'))
    $htmlTag = [regex]::Match($documentHtml, '(?is)<html\b[^>]*>')
    $lang = if ($htmlTag.Success) {
        Get-HtmlAttributeValue -Tag $htmlTag.Value -Name 'lang'
    }
    else {
        ''
    }

    $descriptionTags = @($metaTags | Where-Object {
        (Get-HtmlAttributeValue -Tag $_.Value -Name 'name') -ieq 'description'
    })
    $robotsTags = @($metaTags | Where-Object {
        (Get-HtmlAttributeValue -Tag $_.Value -Name 'name') -ieq 'robots'
    })
    $ogTitleTags = @($metaTags | Where-Object {
        (Get-HtmlAttributeValue -Tag $_.Value -Name 'property') -ieq 'og:title'
    })
    $ogDescriptionTags = @($metaTags | Where-Object {
        (Get-HtmlAttributeValue -Tag $_.Value -Name 'property') -ieq 'og:description'
    })
    $ogUrlTags = @($metaTags | Where-Object {
        (Get-HtmlAttributeValue -Tag $_.Value -Name 'property') -ieq 'og:url'
    })
    $ogImageTags = @($metaTags | Where-Object {
        (Get-HtmlAttributeValue -Tag $_.Value -Name 'property') -ieq 'og:image'
    })
    $canonicalTags = @($linkTags | Where-Object {
        (Get-HtmlAttributeValue -Tag $_.Value -Name 'rel') -ieq 'canonical'
    })
    $alternateTags = @($linkTags | Where-Object {
        (Get-HtmlAttributeValue -Tag $_.Value -Name 'rel') -ieq 'alternate'
    })

    $title = if ($titleTags.Count -eq 1) {
        [System.Net.WebUtility]::HtmlDecode($titleTags[0].Groups['value'].Value).Trim()
    }
    else {
        ''
    }
    $description = if ($descriptionTags.Count -eq 1) {
        Get-HtmlAttributeValue -Tag $descriptionTags[0].Value -Name 'content'
    }
    else {
        ''
    }
    $canonicalHref = if ($canonicalTags.Count -eq 1) {
        Get-HtmlAttributeValue -Tag $canonicalTags[0].Value -Name 'href'
    }
    else {
        ''
    }
    $expectedPath = ([uri]$PageUri).AbsolutePath
    $expectedCanonical = $CanonicalOrigin.TrimEnd('/') + $(if ($expectedPath -eq '/') { '/' } else { $expectedPath })
    $expectedEnglish = "${expectedCanonical}?culture=en"

    $alternateValues = @{}
    $duplicateAlternate = $false
    foreach ($tag in $alternateTags) {
        $language = Get-HtmlAttributeValue -Tag $tag.Value -Name 'hreflang'
        if ([string]::IsNullOrWhiteSpace($language)) {
            continue
        }

        if ($alternateValues.ContainsKey($language)) {
            $duplicateAlternate = $true
            continue
        }

        $alternateValues[$language] = Get-HtmlAttributeValue -Tag $tag.Value -Name 'href'
    }

    $robotsContent = @($robotsTags | ForEach-Object {
        Get-HtmlAttributeValue -Tag $_.Value -Name 'content'
    }) -join ','
    $jsonLdTags = @([regex]::Matches(
        $Html,
        '(?is)<script\b[^>]*type\s*=\s*["'']application/ld\+json["''][^>]*>(?<json>.*?)</script>'))
    $invalidJsonLd = 0
    foreach ($tag in $jsonLdTags) {
        try {
            $null = $tag.Groups['json'].Value | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            $invalidJsonLd++
        }
    }

    $titlePassed = $titleTags.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace($title)
    $descriptionPassed = $descriptionTags.Count -eq 1 -and $description.Trim().Length -ge 40
    $robotsPassed = $robotsTags.Count -le 1 -and $robotsContent -notmatch '(?i)\b(?:noindex|nofollow)\b'
    $canonicalPassed = $canonicalTags.Count -eq 1 -and $canonicalHref -ceq $expectedCanonical
    $alternatePassed = $alternateValues.Count -eq 3 `
        -and -not $duplicateAlternate `
        -and $alternateValues.ContainsKey('en') `
        -and $alternateValues.ContainsKey('th') `
        -and $alternateValues.ContainsKey('x-default') `
        -and $alternateValues['en'] -ceq $expectedEnglish `
        -and $alternateValues['th'] -ceq $expectedCanonical `
        -and $alternateValues['x-default'] -ceq $expectedCanonical
    $openGraphCorePassed = $ogTitleTags.Count -eq 1 -and $ogDescriptionTags.Count -eq 1 `
        -and -not [string]::IsNullOrWhiteSpace((Get-HtmlAttributeValue -Tag $ogTitleTags[0].Value -Name 'content')) `
        -and -not [string]::IsNullOrWhiteSpace((Get-HtmlAttributeValue -Tag $ogDescriptionTags[0].Value -Name 'content'))
    $openGraphUrlPassed = -not $OpenGraphUrlRequired -or (
        $ogUrlTags.Count -eq 1 -and
        (Get-HtmlAttributeValue -Tag $ogUrlTags[0].Value -Name 'content') -ceq $expectedCanonical)
    $openGraphImagePassed = -not $OpenGraphImageRequired -or (
        $ogImageTags.Count -eq 1 -and
        -not [string]::IsNullOrWhiteSpace((Get-HtmlAttributeValue -Tag $ogImageTags[0].Value -Name 'content')))
    $openGraphPassed = $openGraphCorePassed -and $openGraphUrlPassed -and $openGraphImagePassed
    $headingPassed = $h1Tags.Count -eq 1 -or ($AllowMultipleH1 -and $h1Tags.Count -gt 0)
    $jsonLdPassed = $invalidJsonLd -eq 0 `
        -and ((-not $JsonLdRequired) -or ($jsonLdTags.Count -gt 0)) `
        -and ((-not $JsonLdRequired) -or ($Html -match '(?i)https://schema\.org'))
    $passed = $titlePassed `
        -and $descriptionPassed `
        -and $robotsPassed `
        -and $canonicalPassed `
        -and $alternatePassed `
        -and $openGraphPassed `
        -and $headingPassed `
        -and -not [string]::IsNullOrWhiteSpace($lang) `
        -and $jsonLdPassed

    $actual = [ordered]@{
        pageUri = $PageUri
        titleCount = $titleTags.Count
        descriptionCount = $descriptionTags.Count
        descriptionLength = $description.Trim().Length
        robotsCount = $robotsTags.Count
        robots = $robotsContent
        canonicalCount = $canonicalTags.Count
        canonical = $canonicalHref
        expectedCanonical = $expectedCanonical
        alternateCount = $alternateTags.Count
        alternateLanguages = @($alternateValues.Keys)
        h1Count = $h1Tags.Count
        lang = $lang
        jsonLdCount = $jsonLdTags.Count
        invalidJsonLd = $invalidJsonLd
        titlePassed = $titlePassed
        descriptionPassed = $descriptionPassed
        robotsPassed = $robotsPassed
        canonicalPassed = $canonicalPassed
        alternatePassed = $alternatePassed
        openGraphPassed = $openGraphPassed
        ogUrlCount = $ogUrlTags.Count
        ogImageCount = $ogImageTags.Count
        headingPassed = $headingPassed
        languagePassed = -not [string]::IsNullOrWhiteSpace($lang)
        jsonLdPassed = $jsonLdPassed
    } | ConvertTo-Json -Compress

    return New-ContractResult `
        -Name 'public_page_seo' `
        -Passed $passed `
        -Expected '200 public document with title, description, indexable robots, canonical, hreflang, Open Graph, H1, language, and valid JSON-LD' `
        -Actual $actual
}

function Test-EnglishCookieCanonicalRedirectContract {
    param(
        [Parameter(Mandatory = $true)]
        [int]$StatusCode,

        [AllowEmptyString()]
        [string]$Location = '',

        [Parameter(Mandatory = $true)]
        [string]$ExpectedLocation
    )

    $passed = $StatusCode -eq 301 -and $Location -ceq $ExpectedLocation
    return New-ContractResult `
        -Name 'english_cookie_canonical_redirect' `
        -Passed $passed `
        -Expected "301 Location: $ExpectedLocation" `
        -Actual "$StatusCode Location: $Location"
}

function Find-JsonLdNode {
    param(
        [AllowNull()]
        [object]$Value
    )

    if ($null -eq $Value -or $Value -is [string]) {
        return
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        foreach ($item in $Value) {
            Find-JsonLdNode -Value $item
        }

        return
    }

    if ($Value.PSObject.Properties['@type']) {
        $Value
    }

    foreach ($property in $Value.PSObject.Properties) {
        if ($property.Name -in @('@context', '@type')) {
            continue
        }

        Find-JsonLdNode -Value $property.Value
    }
}

function Test-LocalBusinessHoursContract {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Html
    )

    $localBusinesses = @()
    foreach ($match in [regex]::Matches($Html, '(?is)<script\b[^>]*type\s*=\s*["'']application/ld\+json["''][^>]*>(?<json>.*?)</script>')) {
        try {
            $json = $match.Groups['json'].Value | ConvertFrom-Json
            $localBusinesses += @(Find-JsonLdNode -Value $json | Where-Object { $_.'@type' -eq 'LocalBusiness' })
        }
        catch {
            continue
        }
    }

    $weekdayNames = @('Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday')
    $weekendNames = @('Saturday', 'Sunday')
    $validBusinessCount = 0
    $conflicts = @()

    foreach ($business in $localBusinesses) {
        $specifications = @($business.openingHoursSpecification)
        $days = @($specifications | ForEach-Object { @($_.dayOfWeek) })
        $hasEveryWeekday = @($weekdayNames | Where-Object { $_ -notin $days }).Count -eq 0
        $hasWeekend = @($weekendNames | Where-Object { $_ -in $days }).Count -gt 0
        $wrongTimes = @($specifications | Where-Object {
            $_.opens -ne '09:00' -or $_.closes -ne '18:00'
        })

        if ($hasEveryWeekday -and -not $hasWeekend -and $wrongTimes.Count -eq 0) {
            $validBusinessCount++
        }
        else {
            $conflicts += [ordered]@{
                days = $days
                times = @($specifications | ForEach-Object { "$($_.opens)-$($_.closes)" })
            }
        }
    }

    $passed = $localBusinesses.Count -gt 0 `
        -and $validBusinessCount -eq $localBusinesses.Count `
        -and $conflicts.Count -eq 0
    $actual = [ordered]@{
        localBusinessCount = $localBusinesses.Count
        validBusinessCount = $validBusinessCount
        conflicts = $conflicts
    } | ConvertTo-Json -Depth 5 -Compress

    return New-ContractResult `
        -Name 'local_business_hours' `
        -Passed $passed `
        -Expected 'Every LocalBusiness opens 09:00-18:00 Monday-Friday and is closed Saturday-Sunday' `
        -Actual $actual
}

function Test-PrivateCacheContract {
    param(
        [AllowEmptyString()]
        [string]$CacheControl = ''
    )

    $public = $CacheControl -match '(?i)(^|[,\s])public([,\s=]|$)'
    $privateDirective = $CacheControl -match '(?i)(^|[,\s])(private|no-store)([,\s=]|$)'
    $passed = -not $public -and $privateDirective

    return New-ContractResult `
        -Name 'personalized_cache_policy' `
        -Passed $passed `
        -Expected 'private or no-store, never public' `
        -Actual $CacheControl
}

function Test-AccountNoIndexContract {
    param(
        [int]$StatusCode,

        [AllowEmptyString()]
        [string]$XRobotsTag = '',

        [AllowEmptyString()]
        [string]$Html = ''
    )

    $headerDirectives = @($XRobotsTag.Split(',') | ForEach-Object {
        $_.Trim().ToLowerInvariant()
    } | Where-Object { $_ -ne '' })

    $robotsMeta = @([regex]::Matches(
        $Html,
        '<meta\b[^>]*>',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase) | Where-Object {
        $_.Value -match '(?i)\bname\s*=\s*["'']robots["'']'
    })

    $metaDirective = ''
    if ($robotsMeta.Count -eq 1) {
        $contentMatch = [regex]::Match(
            $robotsMeta[0].Value,
            '(?i)\bcontent\s*=\s*["''](?<value>[^"'']*)["'']')
        if ($contentMatch.Success) {
            $metaDirective = $contentMatch.Groups['value'].Value.Replace(' ', '').ToLowerInvariant()
        }
    }

    $passed = $StatusCode -eq 200 `
        -and $headerDirectives -contains 'noindex' `
        -and $headerDirectives -contains 'follow' `
        -and $robotsMeta.Count -eq 1 `
        -and $metaDirective -eq 'noindex,follow'
    $actual = [ordered]@{
        statusCode = $StatusCode
        xRobotsTag = $XRobotsTag
        robotsMetaCount = $robotsMeta.Count
        robotsMetaDirective = $metaDirective
    } | ConvertTo-Json -Compress

    return New-ContractResult `
        -Name 'account_utility_noindex' `
        -Passed $passed `
        -Expected '200 with X-Robots-Tag noindex, follow and exactly one noindex,follow robots meta' `
        -Actual $actual
}

function Get-ResponseHeaderValue {
    param(
        [AllowNull()]
        [object]$Headers,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($null -eq $Headers) {
        return ''
    }

    foreach ($key in @($Headers.Keys)) {
        if ([string]$key -ine $Name) {
            continue
        }

        $value = $Headers[$key]
        if ($value -is [string]) {
            return $value
        }

        return (@($value) -join ', ')
    }

    return ''
}

function Invoke-ProductionSeoHttpRequest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [Parameter(Mandatory = $true)]
        [bool]$FollowRedirects,

        [hashtable]$Headers = @{}
    )

    $maximumRedirection = if ($FollowRedirects) { 10 } else { 0 }
    $requestErrorAction = if ($FollowRedirects) { 'Stop' } else { 'SilentlyContinue' }
    try {
        $requestHeaders = @{ 'User-Agent' = 'MALIEV-Production-SEO-Verifier/1.0' }
        foreach ($header in $Headers.GetEnumerator()) {
            $requestHeaders[$header.Key] = $header.Value
        }

        return Invoke-WebRequest `
            -Uri $Uri `
            -MaximumRedirection $maximumRedirection `
            -SkipHttpErrorCheck `
            -ErrorAction $requestErrorAction `
            -Headers $requestHeaders
    }
    catch [Microsoft.PowerShell.Commands.HttpResponseException] {
        if ($FollowRedirects -or $null -eq $_.Exception.Response) {
            throw
        }

        $response = $_.Exception.Response
        $headers = @{}
        foreach ($header in $response.Headers) {
            $headers[$header.Key] = @($header.Value) -join ', '
        }

        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Headers = $headers
            Content = ''
        }
    }
}

function Invoke-ProductionSeoVerification {
    param(
        [Parameter(Mandatory = $true)]
        [string]$HttpUri,

        [Parameter(Mandatory = $true)]
        [string]$BaseUri,

        [Parameter(Mandatory = $true)]
        [string[]]$ExpectedPaths,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Request,

        [scriptblock]$LocalizedRequest,

        [string]$ExpectedContainer = 'GTM-KHDDLVRR',

        [string]$ExpectedHsts = 'max-age=63072000; includeSubDomains',

        [string[]]$LegacyIdentifiers = @('GTM-5VBH5LK', 'GTM-P2KSC6C', 'UA-133315708-1')
    )

    $origin = $BaseUri.TrimEnd('/')
    $checks = [System.Collections.Generic.List[object]]::new()

    $httpResponse = & $Request -Uri $HttpUri -FollowRedirects $false
    $checks.Add((Test-HttpRedirectContract `
        -StatusCode ([int]$httpResponse.StatusCode) `
        -Location (Get-ResponseHeaderValue -Headers $httpResponse.Headers -Name 'Location') `
        -ExpectedLocation "$origin/"))

    $homepage = & $Request -Uri "$origin/" -FollowRedirects $true
    $checks.Add((Test-HstsContract `
        -StatusCode ([int]$homepage.StatusCode) `
        -Hsts (Get-ResponseHeaderValue -Headers $homepage.Headers -Name 'Strict-Transport-Security') `
        -ExpectedHsts $ExpectedHsts))
    $checks.Add((Test-HomepageMeasurementContract `
        -Html ([string]$homepage.Content) `
        -ExpectedContainer $ExpectedContainer `
        -LegacyIdentifiers $LegacyIdentifiers))
    $checks.Add((Test-LocalBusinessHoursContract -Html ([string]$homepage.Content)))

    if ($null -ne $LocalizedRequest) {
        $englishCookieResponse = & $LocalizedRequest `
            -Uri "$origin/services/3d-printing" `
            -FollowRedirects $false `
            -Headers @{ Cookie = '.AspNetCore.Culture=c%3Den%7Cuic%3Den' }
        $checks.Add((Test-EnglishCookieCanonicalRedirectContract `
                -StatusCode ([int]$englishCookieResponse.StatusCode) `
                -Location (Get-ResponseHeaderValue -Headers $englishCookieResponse.Headers -Name 'Location') `
                -ExpectedLocation "$origin/services/3d-printing?culture=en"))
    }

    $accountLogin = & $Request -Uri "$origin/account/login" -FollowRedirects $true
    $checks.Add((Test-AccountNoIndexContract `
        -StatusCode ([int]$accountLogin.StatusCode) `
        -XRobotsTag (Get-ResponseHeaderValue -Headers $accountLogin.Headers -Name 'X-Robots-Tag') `
        -Html ([string]$accountLogin.Content)))

    $robotsUri = "$origin/robots.txt"
    $expectedSitemapUri = "$origin/sitemap"
    $robots = & $Request -Uri $robotsUri -FollowRedirects $true
    $robotsSitemapPassed = [int]$robots.StatusCode -eq 200 `
        -and [string]$robots.Content -match "(?im)^\s*Sitemap:\s*$([regex]::Escape($expectedSitemapUri))\s*$"
    $checks.Add((New-ContractResult `
        -Name 'robots_sitemap_discovery' `
        -Passed $robotsSitemapPassed `
        -Expected "200 with Sitemap: $expectedSitemapUri" `
        -Actual "$([int]$robots.StatusCode)"))

    $sitemap = & $Request -Uri $expectedSitemapUri -FollowRedirects $true
    $sitemapContentType = Get-ResponseHeaderValue -Headers $sitemap.Headers -Name 'Content-Type'
    $sitemapHttpPassed = [int]$sitemap.StatusCode -eq 200 `
        -and $sitemapContentType -match '(?i)^application/xml(?:;|$)'
    $checks.Add((New-ContractResult `
        -Name 'sitemap_http_response' `
        -Passed $sitemapHttpPassed `
        -Expected '200 application/xml' `
        -Actual "$([int]$sitemap.StatusCode) $sitemapContentType"))
    $checks.Add((Test-SitemapContract `
        -Xml ([string]$sitemap.Content) `
        -ExpectedPaths $ExpectedPaths `
        -CanonicalOrigin $origin))

    foreach ($path in $ExpectedPaths) {
        $pageUri = if ($path -eq '/') { "$origin/" } else { "$origin$path" }
        $page = if ($path -eq '/') {
            $homepage
        }
        else {
            & $Request -Uri $pageUri -FollowRedirects $true
        }

        $canonicalCheck = Test-CanonicalContract -PageUri $pageUri -Html ([string]$page.Content)
        if ([int]$page.StatusCode -ne 200) {
            $canonicalCheck.Passed = $false
            $canonicalCheck.Actual = "$([int]$page.StatusCode) $($canonicalCheck.Actual)".Trim()
        }

        $canonicalCheck | Add-Member -NotePropertyName PageUri -NotePropertyValue $pageUri
        $checks.Add($canonicalCheck)

        $publicSeoCheck = Test-PublicPageSeoContract `
            -PageUri $pageUri `
            -Html ([string]$page.Content) `
            -CanonicalOrigin $origin `
            -JsonLdRequired (-not $path.StartsWith('/knowledges', [StringComparison]::OrdinalIgnoreCase)) `
            -AllowMultipleH1 ($path -eq '/instantquotation/3d-printing')
        if ([int]$page.StatusCode -ne 200) {
            $publicSeoCheck.Passed = $false
            $publicSeoCheck.Actual = "$([int]$page.StatusCode) $($publicSeoCheck.Actual)".Trim()
        }

        $publicSeoCheck | Add-Member -NotePropertyName PageUri -NotePropertyValue $pageUri
        $checks.Add($publicSeoCheck)

        if ($path -ne '/' -and [int]$page.StatusCode -eq 200) {
            $measurementCheck = Test-RouteMeasurementContract `
                -PageUri $pageUri `
                -Html ([string]$page.Content) `
                -ExpectedContainer $ExpectedContainer `
                -LegacyIdentifiers $LegacyIdentifiers
            $measurementCheck | Add-Member -NotePropertyName PageUri -NotePropertyValue $pageUri
            $checks.Add($measurementCheck)
        }
        elseif ([int]$page.StatusCode -ne 200 -and $path -ne '/') {
            $checks.Add((New-ContractResult `
                -Name 'route_measurement_source' `
                -Passed $false `
                -Expected "Route measurement source check with HTTP 200 status for $pageUri" `
                -Actual "Route returned HTTP $([int]$page.StatusCode)"))
        }

        if ($path -in @('/contact', '/quotation')) {
            $cacheCheck = Test-PrivateCacheContract -CacheControl (
                Get-ResponseHeaderValue -Headers $page.Headers -Name 'Cache-Control')
            $cacheCheck | Add-Member -NotePropertyName PageUri -NotePropertyValue $pageUri
            $checks.Add($cacheCheck)
        }
    }

    return [pscustomobject]@{
        Passed = @($checks | Where-Object { -not $_.Passed }).Count -eq 0
        CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        BaseUri = $origin
        Checks = @($checks)
    }
}

Export-ModuleMember -Function @(
    'Invoke-ProductionSeoHttpRequest',
    'Get-ExpectedPublicSearchPaths',
    'Test-HttpRedirectContract',
    'Test-HstsContract',
    'Test-HomepageMeasurementContract',
    'Test-RouteMeasurementContract',
    'Test-SitemapContract',
    'Test-CanonicalContract',
    'Test-PublicPageSeoContract',
    'Test-EnglishCookieCanonicalRedirectContract',
    'Test-LocalBusinessHoursContract',
    'Test-PrivateCacheContract',
    'Test-AccountNoIndexContract',
    'Invoke-ProductionSeoVerification'
)
