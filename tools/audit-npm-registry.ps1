# Read-only fallback for environments where npm's TLS client cannot reach audit.
$ErrorActionPreference = 'Stop'
$lock = Get-Content (Join-Path (Split-Path -Parent $PSScriptRoot) 'package-lock.json') -Raw | ConvertFrom-Json -AsHashtable
$request = @{}
foreach ($entry in $lock.packages.GetEnumerator()) {
    if ($entry.Key -and $entry.Value.version) {
        $name = $entry.Key.Substring($entry.Key.LastIndexOf('node_modules/') + 13)
        if (-not $request.ContainsKey($name)) { $request[$name] = @() }
        $request[$name] += @($entry.Value.version)
    }
}
$response = Invoke-RestMethod -Uri 'https://registry.npmjs.org/-/npm/v1/security/advisories/bulk' -Method Post -ContentType 'application/json' -Body ($request | ConvertTo-Json -Compress)
$count = @($response.PSObject.Properties).Count
[pscustomobject]@{ PackagesChecked = $request.Count; AdvisoryPackages = $count } | ConvertTo-Json -Compress
if ($count -gt 0) { $response | ConvertTo-Json -Depth 10; exit 1 }
