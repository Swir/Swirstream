param([ValidateSet('Debug','Release')][string]$Mode='Debug')
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    if (-not $env:JAVA_HOME -or -not (Test-Path -LiteralPath (Join-Path $env:JAVA_HOME 'bin/java.exe'))) { throw 'Ustaw JAVA_HOME na zainstalowany JDK 17 lub nowszy zgodny z AGP 9.0.1.' }
    if (-not $env:ANDROID_HOME -and -not (Test-Path -LiteralPath 'local.properties')) { throw 'Ustaw ANDROID_HOME na Android SDK (platforma 36.1, build-tools 36.0.0).' }
    & .\gradlew.bat ":app:assemble$Mode" ':app:testDebugUnitTest' ":app:lint$Mode" --console=plain
    if ($LASTEXITCODE -ne 0) { throw 'Kompilacja lub kontrola nie powiodła się.' }
    if ($Mode -eq 'Release') { Write-Output 'Zbudowano app/build/outputs/apk/release/app-release-unsigned.apk. Podpisz własnym kluczem przed instalacją.' }
    else { Write-Output 'Gotowe: app/build/outputs/apk/debug/app-debug.apk (podpis deweloperski).' }
} finally { Pop-Location }
