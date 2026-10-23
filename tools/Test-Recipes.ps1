param([Parameter(Mandatory=$true)][string]$RepositoryRoot,[Parameter(Mandatory=$true)][string]$RuntimeRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Repository-Paths.ps1')
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
$spec=Get-Content -LiteralPath (Join-Path $repo 'repo.json') -Raw | ConvertFrom-Json
$recipes=Get-Content -LiteralPath (Resolve-RepositoryPath $repo $spec.TestPlanPath) -Raw | ConvertFrom-Json
$bin=Join-Path $repo 'out/tests';New-Item -ItemType Directory -Path $bin -Force | Out-Null
$refs=@(Get-ChildItem -LiteralPath $RuntimeRoot -Filter '*.dll' | Where-Object {$_.Name -match '^(System\.|mscorlib\.|netstandard\.)' -and $_.Name -notmatch '\.Native\.'} | ForEach-Object FullName)
$passed=@()
foreach($recipe in $recipes){
 $sources=@($recipe.Sources | ForEach-Object {Resolve-RepositoryPath $repo $_})
 $references=@($refs)
 foreach($reference in $recipe.References){$file=Resolve-RepositoryPath $repo $reference;$references+=$file;Copy-Item -LiteralPath $file -Destination $bin -Force}
 $target=Join-Path $bin ($recipe.Name+'.dll')
 $resources=@($recipe.Resources | Where-Object {$_} | ForEach-Object {(Resolve-RepositoryPath $repo $_.Path)+'|'+$_.Name})
 & (Join-Path $PSScriptRoot 'Compile.ps1') -Sources $sources -References $references -Resources $resources -Output $target -Executable | Tee-Object -FilePath (Join-Path $bin ($recipe.Name+'-compile.log')) | Out-Null
 @{runtimeOptions=@{tfm='net6.0';framework=@{name='Microsoft.NETCore.App';version=(Split-Path $RuntimeRoot -Leaf)}}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($target,'.runtimeconfig.json')) -Encoding utf8
 $arguments=@($recipe.Arguments | Where-Object {$_} | ForEach-Object {Resolve-RepositoryPath $repo $_})
 $previousFixtureRoot=$env:PRELUNA_TEST_ROOT
 try{
  $env:PRELUNA_TEST_ROOT=Join-Path $bin 'fixtures';New-Item -ItemType Directory -Path $env:PRELUNA_TEST_ROOT -Force | Out-Null
  & dotnet $target @arguments | Tee-Object -FilePath (Join-Path $bin ($recipe.Name+'.log'))
  if($LASTEXITCODE -ne 0){throw ('Test failed: '+$recipe.Name)}
 }finally{$env:PRELUNA_TEST_ROOT=$previousFixtureRoot}
 $passed+=$recipe.Name
}
[ordered]@{Passed=$true;Suites=$passed;GameplayTested=$false} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $bin 'test-results.json') -Encoding utf8
