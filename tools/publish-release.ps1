param(
    [Parameter(Mandatory)][string]$WindowsVersion,
    [Parameter(Mandatory)][string]$AndroidVersion,
    [Parameter(Mandatory)][string]$AssetDir,
    [Parameter(Mandatory)][string]$Notes,
    [Parameter(Mandatory)][string]$Commit
)
$ErrorActionPreference='Stop'
$repo='Swir/Swirstream'
if ($WindowsVersion -notmatch '^\d+\.\d+\.\d+$' -or $AndroidVersion -notmatch '^\d+\.\d+\.\d+$' -or $Commit -notmatch '^[a-f0-9]{40}$') { throw 'Expected semantic versions and a full Git commit.' }
$tag='v'+$WindowsVersion
$dir=(Resolve-Path -LiteralPath $AssetDir).Path
$files=@("SwirStreamAI_${WindowsVersion}_Setup_x64.exe","SwirStreamAI_Android_${AndroidVersion}.apk")
$names=@()
foreach($file in $files){$path=Join-Path $dir $file;if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing $file"};$names+=$path}
$sums=Join-Path $dir 'SHA256SUMS.txt'
$names|ForEach-Object {(Get-FileHash $_).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_)}|Set-Content -LiteralPath $sums -Encoding ascii
$names+=$sums
$remote=& gh api "repos/$repo/git/ref/tags/$tag" --jq '.object.sha'
if($LASTEXITCODE -or $remote -ne $Commit){throw 'Push a lightweight tag at the tested commit first.'}
& gh release create $tag --repo $repo --verify-tag --draft --title "SwirStream AI $WindowsVersion for Windows and $AndroidVersion for Android" --notes-file $Notes @names
if($LASTEXITCODE){throw 'Draft creation or upload failed. Inspect the release before retrying.'}
$metadata=& gh release view $tag --repo $repo --json assets,isDraft|ConvertFrom-Json
if($LASTEXITCODE -or !$metadata.isDraft){throw 'Could not inspect draft metadata.'}
foreach($file in $names){
    $asset=@($metadata.assets|Where-Object name -eq ([IO.Path]::GetFileName($file)))
    if($asset.Count -ne 1 -or $asset[0].size -ne (Get-Item -LiteralPath $file).Length -or $asset[0].digest -ne ('sha256:'+(Get-FileHash $file).Hash.ToLowerInvariant())){throw 'Draft asset integrity mismatch.'}
}
& gh release edit $tag --repo $repo --draft=false --latest
if($LASTEXITCODE){throw 'Could not publish the verified draft.'}
