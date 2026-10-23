param([Parameter(Mandatory=$true)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
if(!(Test-Path -LiteralPath (Join-Path $repo '.git'))){throw 'Select an independent Git repository.'}
& (Join-Path $PSScriptRoot 'Verify-Repository.ps1') -RepositoryRoot $repo | Out-Null
$lock=Get-Content -LiteralPath (Join-Path $repo 'maintenance.lock.json') -Raw|ConvertFrom-Json
$spec=Get-Content -LiteralPath (Join-Path $repo 'repo.json') -Raw|ConvertFrom-Json
$expected=@($lock.Files.Path)+@('maintenance.lock.json')
$tracked=@(& git -c core.quotepath=false -C $repo ls-files)
if($LASTEXITCODE -ne 0){throw 'Cannot read Git index.'}
if(@(Compare-Object $expected $tracked).Count){throw 'Git index differs from maintenance.lock.json. Review additions/removals and update the lock explicitly.'}
$unstaged=@(& git -c core.quotepath=false -C $repo diff --name-only)
if($LASTEXITCODE -ne 0 -or $unstaged.Count){throw 'Working files differ from staged files. Review and stage the complete intended change before checking.'}
$untracked=@(& git -c core.quotepath=false -C $repo ls-files --others --exclude-standard)
if($LASTEXITCODE -ne 0 -or $untracked.Count){throw 'Untracked files remain. Include intentional inputs or ignore local outputs.'}
$baselinePaths=@($spec.Modules | Where-Object Kind -eq 'frozen-migrated' | ForEach-Object Baseline)
foreach($relative in $tracked){
 if($relative -match '(^|/)(out|dist|artifacts|bin|obj|\.git|\.local|研究资料|预计mod|测试部署[^/]*)/|(^|/)\.env($|\.)|\.(cfg|pdb|zip|7z|log|pfx|p12|pem|key)$'){throw ('Disallowed public input: '+$relative)}
 if($relative -like '*.dll' -and $relative -notin $baselinePaths){throw ('Only declared frozen input DLLs may be tracked: '+$relative)}
 if($relative -notlike '*.dll'){
  $body=[IO.File]::ReadAllText((Join-Path $repo $relative))
  if($body -match '(ghp_[A-Za-z0-9]{36}|github_pat_[A-Za-z0-9_]{50,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----)'){throw ('Credential-shaped content detected: '+$relative)}
 }
}
foreach($module in $spec.Modules | Where-Object Kind -eq 'frozen-migrated'){
 if((Get-FileHash -LiteralPath (Join-Path $repo $module.Baseline)).Hash -cne $module.BaselineSHA256){throw ('Frozen input changed: '+$module.Id)}
}
& git -C $repo check-ignore 'out/build-manifest.json' '.env' 'dist/package.zip' | Out-Null
if($LASTEXITCODE -ne 0){throw 'Required output/secret ignore rules are missing.'}
[pscustomobject]@{Repository=$spec.Repository;TrackedFiles=$tracked.Count;SourceLockMatchesIndex=$true;FrozenInputsVerified=$baselinePaths.Count;Passed=$true;BuildAndGameplayNotImplied=$true}
