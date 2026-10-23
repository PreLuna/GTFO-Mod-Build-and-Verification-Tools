param(
 [Parameter(Mandatory=$true)][string]$RepositoryRoot,
 [Parameter(Mandatory=$true)][string]$DestinationRoot,
 [Parameter(Mandatory=$true)][string]$Version
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
$destination=[IO.Path]::GetFullPath($DestinationRoot)
if($destination -eq $repo -or $destination.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Release output must be outside the source repository.'}
if($Version -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$'){throw 'Invalid release label.'}
& (Join-Path $PSScriptRoot 'Check-Commit.ps1') -RepositoryRoot $repo | Out-Null
$status=@(& git -C $repo status --porcelain)
if($LASTEXITCODE -ne 0 -or $status.Count){throw 'Commit the verified source before creating a release package.'}
$commit=& git -C $repo rev-parse HEAD
if($LASTEXITCODE -ne 0){throw 'A source commit is required.'}
$build=Get-Content -LiteralPath (Join-Path $repo 'out/build-manifest.json') -Raw | ConvertFrom-Json
$spec=Get-Content -LiteralPath (Join-Path $repo 'repo.json') -Raw | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'Verify-Repository.ps1') -RepositoryRoot $repo -Build -CecilPath (Join-Path $build.GameRoot 'BepInEx/core/Mono.Cecil.dll') | Out-Null
$toolkit=Split-Path $PSScriptRoot -Parent
if($build.ToolchainSourceLockSHA256 -cne (Get-FileHash -LiteralPath (Join-Path $toolkit 'maintenance.lock.json')).Hash){throw 'Rebuild using the current verified toolkit before packaging.'}
$payload=Join-Path $repo 'out/package'
$expected=@{}
foreach($artifact in $build.Artifacts){
 $relative=$artifact.Path.Substring('out/package/'.Length)
 $expected[$relative]=$artifact.SHA256
}
foreach($module in $spec.Modules | Where-Object Kind -eq 'data'){
 foreach($file in Get-ChildItem -LiteralPath (Join-Path $repo 'data') -Recurse -File -Force){
  $relative='BepInEx/plugins/PreLuna.WeaponDataCustom/GameData/'+[IO.Path]::GetRelativePath((Join-Path $repo 'data'),$file.FullName).Replace('\','/')
  $expected[$relative]=(Get-FileHash -LiteralPath $file.FullName).Hash
 }
}
$actual=@(Get-ChildItem -LiteralPath $payload -Recurse -File -Force)
if($actual.Count -ne $expected.Count){throw 'Unexpected or missing package files.'}
foreach($file in $actual){
 $relative=[IO.Path]::GetRelativePath($payload,$file.FullName).Replace('\','/')
 if(!$expected.ContainsKey($relative) -or (Get-FileHash -LiteralPath $file.FullName).Hash -cne $expected[$relative]){throw ('Unverified package file: '+$relative)}
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$zip=Join-Path $destination ($spec.Repository+'-'+$Version+'.zip')
if(Test-Path -LiteralPath $zip){throw 'Refusing to overwrite a release artifact; select a new version label.'}
$stage=Join-Path $repo ('out/release-stage-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try{
 Copy-Item -LiteralPath (Join-Path $payload 'BepInEx') -Destination $stage -Recurse
 Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination (Join-Path $stage 'INSTALL.md')
 [IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
 $manifest=[ordered]@{Repository=$spec.Repository;Version=$Version;SourceCommit=$commit;SourceLockSHA256=$build.SourceLockSHA256;ToolchainSourceLockSHA256=$build.ToolchainSourceLockSHA256;Zip=[IO.Path]::GetFileName($zip);SHA256=(Get-FileHash -LiteralPath $zip).Hash;Files=$expected;GameplayTested=$false}
 $manifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath ($zip+'.manifest.json') -Encoding utf8
 $manifest|Select-Object Repository,Version,SourceCommit,SHA256
}finally{
 $resolved=[IO.Path]::GetFullPath($stage)
 if(!$resolved.StartsWith($repo+'\out\release-stage-',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe staging cleanup path.'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
