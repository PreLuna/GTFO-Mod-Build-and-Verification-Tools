param([string]$CheckoutRoot=([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))))
$ErrorActionPreference='Stop'
$pwshPath=(Get-Command pwsh -ErrorAction Stop).Source.Replace('\','/')
$quotedPwsh="'"+$pwshPath.Replace("'","'\''")+"'"
$template=@'
#!/bin/sh
# PreLuna source/index verification. Installed locally; not a tracked hook.
repo="$(git rev-parse --show-toplevel)" || exit 1
tool="$(dirname "$repo")/GTFO-Mod-Build-and-Verification-Tools/tools/Check-Commit.ps1"
if [ ! -f "$tool" ]; then
  echo "PreLuna: shared verification toolkit is missing next to this repository." >&2
  exit 1
fi
__PWSH__ -NoLogo -NoProfile -NonInteractive -File "$tool" -RepositoryRoot "$repo"
exit $?
'@
$count=0
foreach($repo in Get-ChildItem -LiteralPath $CheckoutRoot -Directory | Where-Object Name -like 'GTFO-*'){
 if(!(Test-Path -LiteralPath (Join-Path $repo.FullName '.git'))){continue}
 $custom=@(& git -C $repo.FullName config --get core.hooksPath)
 if($custom.Count){throw ('Existing core.hooksPath must be integrated explicitly: '+$repo.Name)}
 $hook=Join-Path $repo.FullName '.git/hooks/pre-commit'
 if((Test-Path -LiteralPath $hook) -and (Get-Content -LiteralPath $hook -Raw) -notmatch '# PreLuna source/index verification'){
  throw ('Existing pre-commit hook was preserved: '+$repo.Name)
 }
 New-Item -ItemType Directory -Path (Split-Path $hook -Parent) -Force | Out-Null
 [IO.File]::WriteAllText($hook,$template.Replace('__PWSH__',$quotedPwsh).Replace("`r`n","`n")+"`n",[Text.UTF8Encoding]::new($false))
 $count++
}
Write-Output ('Installed source/index pre-commit checks for '+$count+' repositories. Builds and gameplay still require separate verification.')
