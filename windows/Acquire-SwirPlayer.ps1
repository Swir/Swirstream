param([string]$CacheRoot=(Join-Path $PSScriptRoot 'dependency-cache-v070'), [string]$Destination=(Join-Path $PSScriptRoot 'dependency-cache-v070\player'))
$ErrorActionPreference='Stop'
$CacheRoot=[IO.Path]::GetFullPath($CacheRoot)
$Destination=[IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $CacheRoot,$Destination,(Join-Path $CacheRoot 'sources'),(Join-Path $Destination 'licenses') | Out-Null
function Fetch-Verified([string]$Uri,[string]$Path,[string]$Sha256) {
    if (!(Test-Path -LiteralPath $Path)) { Invoke-WebRequest -Uri $Uri -OutFile $Path }
    $actual=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -ne $Sha256) { throw "Checksum mismatch: $Path. File has not been installed." }
}
$release='https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261107'
$archive=Join-Path $CacheRoot 'mpv-dev-x86_64-20261107.7z'
Fetch-Verified "$release/mpv-dev-x86_64-20261107-git-9028187538.7z" $archive 'df7c433a2d481fdaf6163b201f26abbb0eff0c215c51368397088c6d8f7a001b'
$extracted=Join-Path $CacheRoot 'mpv-dev'
New-Item -ItemType Directory -Force -Path $extracted | Out-Null
& tar -xf $archive -C $extracted
if($LASTEXITCODE -ne 0){throw 'Cannot extract mpv archive.'}
$dll=Join-Path $extracted 'libmpv-2.dll'
$dllHash='c91949577ae9cc2d500208da51f2c4a24cfa7a02d0ce6e130bf67ec738b450bb'
if((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -ne $dllHash){throw 'Native DLL checksum mismatch.'}
Copy-Item -LiteralPath $dll -Destination (Join-Path $Destination 'libmpv-2.dll') -Force
Set-Content -LiteralPath (Join-Path $Destination 'libmpv-2.dll.sha256') -Value $dllHash -Encoding ascii
$sources=@(
    @{project='mpv';repository='mpv-player/mpv';revision='902818753849d9ad8f0096fad6ca5aeaa37113b9';sha256='2c87e1ea0825a0eee9cf3f66c79518789c1366fd4331a3cf159d089b9cb1456e'},
    @{project='ffmpeg';repository='FFmpeg/FFmpeg';revision='e196e57ca225a98654e482806fa714facd232b57';sha256='1bd3a6e36c2c2546848c299595f310db104ad479af1af0536a2cd881b820aeb0'},
    @{project='mpv-winbuild-cmake';repository='shinchiro/mpv-winbuild-cmake';revision='bda10a2816ce9908d419b3f4b564227330457e76';sha256='79daa1a5b5389a89b2a553a49d6d696373e181cc68488780b708c7f91808ba89'}
)
$inspect=Join-Path $CacheRoot 'license-inspect'
New-Item -ItemType Directory -Force -Path $inspect | Out-Null
foreach($source in $sources){
    $source.url="https://codeload.github.com/$($source.repository)/tar.gz/$($source.revision)"
    $source.archive="$($source.project)-$($source.revision).tar.gz"
    $path=Join-Path (Join-Path $CacheRoot 'sources') $source.archive
    Fetch-Verified $source.url $path $source.sha256
    if($source.project -eq 'mpv'){
        & tar -xf $path -C $inspect "mpv-$($source.revision)/LICENSE.GPL" "mpv-$($source.revision)/LICENSE.LGPL" "mpv-$($source.revision)/Copyright"
        if($LASTEXITCODE -ne 0){throw 'Cannot extract mpv licenses.'}
        Copy-Item -LiteralPath (Join-Path $inspect "mpv-$($source.revision)/LICENSE.GPL") -Destination (Join-Path $Destination 'licenses/mpv-GPL-2.0.txt') -Force
        Copy-Item -LiteralPath (Join-Path $inspect "mpv-$($source.revision)/LICENSE.LGPL") -Destination (Join-Path $Destination 'licenses/mpv-LGPL-2.1.txt') -Force
        Copy-Item -LiteralPath (Join-Path $inspect "mpv-$($source.revision)/Copyright") -Destination (Join-Path $Destination 'licenses/mpv-Copyright.txt') -Force
    }
    if($source.project -eq 'ffmpeg'){
        & tar -xf $path -C $inspect "FFmpeg-$($source.revision)/COPYING.GPLv3" "FFmpeg-$($source.revision)/LICENSE.md"
        if($LASTEXITCODE -ne 0){throw 'Cannot extract FFmpeg licenses.'}
        Copy-Item -LiteralPath (Join-Path $inspect "FFmpeg-$($source.revision)/COPYING.GPLv3") -Destination (Join-Path $Destination 'licenses/FFmpeg-GPL-3.0.txt') -Force
        Copy-Item -LiteralPath (Join-Path $inspect "FFmpeg-$($source.revision)/LICENSE.md") -Destination (Join-Path $Destination 'licenses/FFmpeg-LICENSE.md') -Force
    }
}
$metadata=[ordered]@{
    selectionReviewedUtc='2026-11-07'; engine='libmpv'; architecture='Windows x86_64 baseline (not x86_64-v3)'
    publisher='shinchiro (third-party Windows build linked by mpv.io)'; release='20261107'
    releasePage='https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261107'
    officialInstallPage='https://mpv.io/installation/'
    buildRun='https://github.com/shinchiro/mpv-winbuild-cmake/actions/runs/42315459116'
    buildRecipeRevision='bda10a2816ce9908d419b3f4b564227330457e76'
    archiveUrl="$release/mpv-dev-x86_64-20261107-git-9028187538.7z"
    archiveSha256='df7c433a2d481fdaf6163b201f26abbb0eff0c215c51368397088c6d8f7a001b'; dllSha256=$dllHash
    observedRuntime='mpv v0.42.0-3-g902818753 / FFmpeg N-128545-ge196e57ca'
    license='mpv GPL-2.0-or-later; linked FFmpeg build enables GPL and version3. Combined binary is subject to GPL-3.0-or-later and applicable third-party notices; it is not an LGPL-only build.'
    sourceBundleComplete=$false
    sourceBundleLimitations='Exact mpv, FFmpeg and build recipe source archives are supplied. Upstream release assets do not include every statically linked dependency source/revision or full license inventory. Build log artifact requires authenticated GitHub access (unauthenticated request returned HTTP 401). The recipe contains floating dependency repositories. This is NOT verified complete corresponding source.'
    sources=$sources
}
$metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Destination 'PROVENANCE.json') -Encoding utf8
@'
Swir Player native engine

The native decoder is libmpv with FFmpeg, a third-party Windows x64 build by shinchiro.
It is loaded inside SwirStream AI; no VLC installation or separate player process is used.
Runtime verified: mpv v0.42.0-3-g902818753 / FFmpeg N-128545-ge196e57ca.

This build is GPL, not LGPL-only. FFmpeg was configured with --enable-gpl and
--enable-version3. See licenses/ and PROVENANCE.json. The Swir wrapper is separate
original source; its permissive license does not remove native-engine obligations.

Source archive coverage is PARTIAL: exact mpv, FFmpeg and build recipe revisions
are available in the matching sources bundle. They do not establish that every
statically linked dependency or corresponding source obligation is covered.
Do not describe this bundle as complete corresponding source or as a fully
reproducible native build. No native compilation was performed by this script;
the verified upstream binary was acquired and tested on Windows.

Do not update the DLL alone. Update the pinned archive and DLL hashes, sources,
notices, then rerun all native tests. Quiet probes depend on the fingerprint filter
in this pinned mpv build; a future mpv version may remove that filter.
'@ | Set-Content -LiteralPath (Join-Path $Destination 'NOTICE.txt') -Encoding utf8
Write-Output "Verified Swir Player engine prepared: $Destination"
