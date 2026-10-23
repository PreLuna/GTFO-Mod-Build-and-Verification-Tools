param(
 [Parameter(Mandatory=$true)][string]$RepositoryRoot,
 [Parameter(Mandatory=$true)][string]$GameRoot,
 [Parameter(Mandatory=$true)][string]$RuntimeRoot,
 [string]$DependencyRoot='', [string]$CecilPath=''
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
$spec=Get-Content -LiteralPath (Join-Path $repo 'repo.json') -Raw | ConvertFrom-Json
$toolkit=Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Verify-Repository.ps1') -RepositoryRoot $repo | Out-Null
& (Join-Path $PSScriptRoot 'Verify-Repository.ps1') -RepositoryRoot $toolkit | Out-Null
$GameRoot=[IO.Path]::GetFullPath($GameRoot);$RuntimeRoot=[IO.Path]::GetFullPath($RuntimeRoot)
if(!$DependencyRoot){$DependencyRoot=Join-Path $GameRoot 'BepInEx/plugins'}
if(!$CecilPath){$CecilPath=Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll'}
$out=Join-Path $repo 'out'
if(Test-Path -LiteralPath (Join-Path $out 'build-manifest.json')){throw 'Existing build found. Select a fresh checkout or explicitly clear its ignored out directory.'}
New-Item -ItemType Directory -Path $out -Force | Out-Null
$compile=Join-Path $PSScriptRoot 'Compile.ps1'
$system=@(Get-ChildItem -LiteralPath $RuntimeRoot -Filter '*.dll' | Where-Object {$_.Name -match '^(System\.|mscorlib\.|netstandard\.)' -and $_.Name -notmatch '\.Native\.'} | ForEach-Object FullName)
if(!$system.Count){throw 'RuntimeRoot must contain .NET 6 reference/runtime assemblies.'}
$game=@(Get-ChildItem -LiteralPath (Join-Path $GameRoot 'BepInEx/interop') -Filter '*.dll' | Where-Object {$_.Name -notin @('netstandard.dll','Newtonsoft.Json.dll')} | ForEach-Object FullName)
foreach($name in @('BepInEx.Core.dll','BepInEx.Unity.IL2CPP.dll','Il2CppInterop.Runtime.dll','0Harmony.dll','SemanticVersioning.dll')){$game+=Join-Path $GameRoot ('BepInEx/core/'+$name)}
$baseRefs=@($system+$game)
$support=Join-Path $out '_support';New-Item -ItemType Directory -Path $support -Force | Out-Null
$migrationNeeded=@($spec.Modules | Where-Object {$_.Kind -in @('managed-migrated','frozen-migrated')}).Count -gt 0
if($migrationNeeded){
 & $compile -Sources @((Join-Path $toolkit 'src/BrandTransform.cs')) -References @($system+$CecilPath) -Output (Join-Path $support 'BrandTransform.dll') -Executable | Out-Null
 Copy-Item -LiteralPath $CecilPath -Destination $support
 @{runtimeOptions=@{tfm='net6.0';framework=@{name='Microsoft.NETCore.App';version=(Split-Path $RuntimeRoot -Leaf)}}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $support 'BrandTransform.runtimeconfig.json') -Encoding utf8
 & $compile -Sources @((Join-Path $toolkit 'src/ModuleMigration.cs')) -References $baseRefs -Output (Join-Path $support 'Migration.helper.dll') | Out-Null
}
$outputs=@{};$artifacts=@()
foreach($module in $spec.Modules){
 if($module.Kind -eq 'data'){
   $target=Join-Path $out 'package/BepInEx/plugins/PreLuna.WeaponDataCustom/GameData'
   New-Item -ItemType Directory -Path $target -Force | Out-Null
   Copy-Item -Path (Join-Path $repo 'data/*') -Destination $target -Recurse
   continue
 }
 $destination=Join-Path $out ('package/BepInEx/plugins/'+$module.Assembly)
 if($module.Kind -eq 'contract'){$destination=Join-Path $out 'package/BepInEx/plugins/PreLuna.ThirdPerson'}
 New-Item -ItemType Directory -Path $destination -Force | Out-Null
 $dll=Join-Path $destination ($module.Assembly+'.dll')
 if($module.Kind -eq 'frozen-migrated'){
   $inputAssembly=Join-Path $repo $module.Baseline
   if((Get-FileHash -LiteralPath $inputAssembly).Hash -cne $module.BaselineSHA256){throw ('Frozen baseline changed: '+$module.Id)}
 }else{
   $sources=@(Get-ChildItem -LiteralPath (Join-Path $repo ('src/'+$module.Id)) -Filter '*.cs' -Recurse -File | ForEach-Object FullName)
   $references=if($module.Kind -eq 'contract'){@($system)}else{@($baseRefs)}
   foreach($required in $module.CompileDependencies){if(!$outputs.ContainsKey($required)){throw ('Missing compile dependency '+$required)};$references+=$outputs[$required]}
   foreach($external in $module.ExternalReferences){
      $matches=@(Get-ChildItem -LiteralPath $DependencyRoot -Filter $external.Name -File -Recurse | Where-Object {(Get-FileHash -LiteralPath $_.FullName).Hash -ceq $external.SHA256})
      if(!$matches.Count){throw ('Missing pinned dependency: '+$external.Name)}
      $references+=$matches[0].FullName
   }
   $resources=@($module.Resources | Where-Object {$_} | ForEach-Object {(Join-Path $repo $_.Path)+'|'+$_.Name})
   $inputAssembly=if($module.Kind -eq 'managed-migrated'){Join-Path $support ($module.Assembly+'.dll')}else{$dll}
   & $compile -Sources $sources -References $references -Resources $resources -Output $inputAssembly -NullableEnabled:([bool]$module.NullableEnabled) -WarningsAsErrors:([bool]$module.WarningsAsErrors) | Out-Null
 }
 if($module.Kind -in @('managed-migrated','frozen-migrated')){
   & dotnet (Join-Path $support 'BrandTransform.dll') $inputAssembly $dll (Join-Path $support 'Migration.helper.dll') (Join-Path $GameRoot 'BepInEx') $RuntimeRoot $module.LegacyGuid $module.Guid (Join-Path $support ($module.Id+'.identity.json')) | Out-Null
   if($LASTEXITCODE -ne 0){throw ('Identity transformation failed: '+$module.Id)}
 }
 $outputs[$module.Id]=$dll
 $artifacts+=[ordered]@{Id=$module.Id;Assembly=$module.Assembly;Path=[IO.Path]::GetRelativePath($repo,$dll).Replace('\','/');SHA256=(Get-FileHash -LiteralPath $dll).Hash}
}
$manifest=[ordered]@{Repository=$spec.Repository;SourceLockSHA256=(Get-FileHash -LiteralPath (Join-Path $repo 'maintenance.lock.json')).Hash;ToolchainSourceLockSHA256=(Get-FileHash -LiteralPath (Join-Path $toolkit 'maintenance.lock.json')).Hash;BuiltAtUtc=[DateTime]::UtcNow.ToString('o');Artifacts=$artifacts;GameRoot=$GameRoot;RuntimeRoot=$RuntimeRoot;GameFilesWritten=$false;GameplayTested=$false}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $out 'build-manifest.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'Verify-Repository.ps1') -RepositoryRoot $repo -Build -CecilPath $CecilPath
