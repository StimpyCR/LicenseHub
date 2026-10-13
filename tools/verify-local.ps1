$ErrorActionPreference = 'Stop'
# HTTPS loopback con certificado de desarrollo confiable; se valida TLS normalmente.
$baseUrl = 'https://localhost:8443'
foreach ($path in @('/health/live', '/health/ready')) {
    $response = Invoke-WebRequest "$baseUrl$path" -TimeoutSec 15
    if ($response.StatusCode -ne 200 -or $response.Content.Trim() -ne 'Healthy') {
        throw "La sonda $path no está saludable."
    }
    if ($response.Headers['Cache-Control'] -notmatch 'no-store') {
        throw "La sonda $path debe impedir caché."
    }
    Write-Host "$path : 200 Healthy"
}
$page = Invoke-WebRequest "$baseUrl/" -TimeoutSec 15
if ($page.StatusCode -ne 200 -or $page.Content -notmatch '<app-root') {
    throw 'No se recibió el documento Angular.'
}
if ($page.Headers['Content-Security-Policy'] -notmatch "frame-ancestors 'none'") {
    throw 'Falta la política CSP de la aplicación web.'
}
$bundleMatches = [regex]::Matches($page.Content, '<script[^>]*src="([^"]+)"')
if ($bundleMatches.Count -eq 0) { throw 'No se encontraron bundles JavaScript.' }
foreach ($bundleMatch in $bundleMatches) {
    $bundle = Invoke-WebRequest "$baseUrl/$($bundleMatch.Groups[1].Value)" -TimeoutSec 15
    if ($bundle.StatusCode -ne 200 -or $bundle.Headers['Content-Type'] -notmatch 'javascript') {
        throw 'No se pudo servir un bundle Angular.'
    }
}
foreach ($path in @('/manifest.webmanifest', '/ngsw.json', '/ngsw-worker.js')) {
    $asset = Invoke-WebRequest "$baseUrl$path" -TimeoutSec 15
    if ($asset.StatusCode -ne 200 -or $asset.Headers['Content-Type'] -match 'text/html') {
        throw "No se recibió el recurso PWA $path."
    }
}
Write-Host 'Frontend, bundles, CSP y recursos PWA: correctos por HTTPS.'
