param(
    [string]$Destination = (Join-Path $PSScriptRoot 'dist\SwirStreamAI_0.9.8'),
    [string]$DependencyCache = (Join-Path $PSScriptRoot 'dependency-cache-v070'),
    [string]$Packages = (Join-Path $PSScriptRoot '..\packages')
)
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
$DependencyCache = [IO.Path]::GetFullPath($DependencyCache)
$Packages = [IO.Path]::GetFullPath($Packages)
$project = Join-Path $PSScriptRoot 'SwirStreamAI.csproj'
$acquire = Join-Path $PSScriptRoot 'Acquire-SwirPlayer.ps1'

# Never overwrite an existing installation or leave old dependencies in the package.
if ($Destination.TrimEnd('\','/') -eq ([IO.Path]::GetPathRoot($Destination)).TrimEnd('\','/') -or
    $Destination.TrimEnd('\','/') -eq ([IO.Path]::GetFullPath($PSScriptRoot)).TrimEnd('\','/')) {
    throw 'Wybierz nowy podfolder wydania, nie katalog główny ani katalog źródeł.'
}
if (Test-Path -LiteralPath $Destination) {
    if (!(Test-Path -LiteralPath $Destination -PathType Container) -or
        @(Get-ChildItem -LiteralPath $Destination -Force | Select-Object -First 1).Count -gt 0) {
        throw 'Folder docelowy musi być nowy lub pusty. Nie nadpisuj istniejącej aplikacji; wybierz inną ścieżkę -Destination.'
    }
}
if (!(Test-Path -LiteralPath $acquire)) { throw 'Brak Acquire-SwirPlayer.ps1 obok build.ps1.' }
Get-Command dotnet -ErrorAction Stop | Out-Null
Get-Command tar -ErrorAction Stop | Out-Null
$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Nie udało się odczytać wersji SDK .NET.' }

& dotnet publish $project -c Release -r win-x64 --self-contained true --source https://api.nuget.org/v3/index.json "-p:RestorePackagesPath=$Packages" -o $Destination
if ($LASTEXITCODE -ne 0) { throw 'Kompilacja aplikacji nie powiodła się. Kolejna próba wymaga nowego lub pustego folderu.' }
if ((Test-Path -LiteralPath (Join-Path $Destination 'vlc')) -or (Test-Path -LiteralPath (Join-Path $Destination 'libvlc.dll'))) {
    throw 'Publikacja zawiera stary silnik. Wydanie 0.9.8 wymaga nowego folderu player.'
}

# Acquires and checks the pinned upstream binary; does not compile the native engine.
& $acquire -CacheRoot $DependencyCache -Destination (Join-Path $Destination 'player')
$native = Get-Content -LiteralPath (Join-Path $Destination 'player\PROVENANCE.json') -Raw | ConvertFrom-Json
$sourceDir = Join-Path $Destination 'third-party-source'
New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null
foreach ($source in $native.sources) {
    Copy-Item -LiteralPath (Join-Path (Join-Path $DependencyCache 'sources') $source.archive) -Destination $sourceDir
}
Copy-Item -LiteralPath (Join-Path $Destination 'player\PROVENANCE.json') -Destination (Join-Path $sourceDir 'PROVENANCE.json')
Copy-Item -LiteralPath (Join-Path $Destination 'player\NOTICE.txt') -Destination (Join-Path $sourceDir 'READ_SOURCE_LIMITATIONS.txt')
foreach ($name in @('README.md','ZACZNIJ_TUTAJ.txt','MODEL_CARD.md','THIRD_PARTY.txt','LICENSE.txt','TESTUJ_WINDOWS.cmd','MAINTENANCE.md','PRIVATE_SOURCES.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $Destination
}

# Copy notices for exactly the runtime versions selected by this publish.
$runtime = Get-Content -LiteralPath (Join-Path $Destination 'SwirStreamAI.runtimeconfig.json') -Raw | ConvertFrom-Json
$runtimePacks = @{
    'Microsoft.NETCore.App' = 'microsoft.netcore.app.runtime.win-x64'
    'Microsoft.WindowsDesktop.App' = 'microsoft.windowsdesktop.app.runtime.win-x64'
}
foreach ($frameworkName in $runtimePacks.Keys) {
    $framework = @($runtime.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq $frameworkName })
    if ($framework.Count -ne 1) { throw "Brak jednoznacznej wersji dołączonego runtime: $frameworkName" }
    $packageId = $runtimePacks[$frameworkName]
    $packagePath = Join-Path (Join-Path $Packages $packageId) $framework[0].version
    $notices = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY)' })
    if (!($notices | Where-Object { $_.Name -match '^LICENSE' })) { throw "Brak licencji runtime: $packagePath" }
    $licenseDir = Join-Path $Destination ('licenses\' + $packageId + '\' + $framework[0].version)
    New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
    $notices | Copy-Item -Destination $licenseDir
}

[ordered]@{
    application = 'SwirStream AI'; version = '0.9.8'; target = 'win-x64'; selfContained = $true
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); dotnetSdk = $sdkVersion
    runtimes = $runtime.runtimeOptions.includedFrameworks
    nativeRelease = $native.release; nativeDllSha256 = $native.dllSha256
    nativeCompiledHere = $false; nativeSourceBundleComplete = $native.sourceBundleComplete
    verification = 'Build and dependency hashes only. Run TESTUJ_WINDOWS.cmd plus release GUI/network/relocation checks; this is not a passing test report.'
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Destination 'build-info.json') -Encoding utf8
Write-Warning 'Natywny silnik GPL ma tylko częściowy zestaw źródeł zależności. Szczegóły: player/PROVENANCE.json oraz THIRD_PARTY.txt. To nie jest zweryfikowane kompletne corresponding source.'
Write-Output "Zbudowano aplikację i przygotowano zależności: $Destination. Testy ani publikacja nie zostały uruchomione przez build.ps1."

