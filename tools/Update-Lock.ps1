param([Parameter(Mandatory=$true)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
# Only tracked files enter the lock; new files must first be reviewed and added.
$tracked=@(& git -c core.quotepath=false -C $repo ls-files)
if($LASTEXITCODE -ne 0 -or !$tracked.Count){throw 'Repository must contain reviewed tracked files.'}
$files=@(foreach($relative in $tracked | Sort-Object){
 if($relative -eq 'maintenance.lock.json'){continue}
 $path=Join-Path $repo $relative
 [ordered]@{Path=$relative;SHA256=(Get-FileHash -LiteralPath $path).Hash;Bytes=(Get-Item -LiteralPath $path).Length}
})
$spec=Get-Content -LiteralPath (Join-Path $repo 'repo.json') -Raw|ConvertFrom-Json
[ordered]@{SchemaVersion=2;Repository=$spec.Repository;Files=$files}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $repo 'maintenance.lock.json') -Encoding utf8
