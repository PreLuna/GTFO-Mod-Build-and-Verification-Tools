param([Parameter(Mandatory=$true)][string]$RepositoryRoot,[switch]$Build,[string]$CecilPath='')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
$lock=Get-Content -LiteralPath (Join-Path $repo 'maintenance.lock.json') -Raw | ConvertFrom-Json
$problems=@()
$lockedPaths=@{};foreach($file in $lock.Files){if($lockedPaths.ContainsKey($file.Path)){throw 'Duplicate locked file.'};$lockedPaths[$file.Path]=$true}
foreach($folder in @('src','baseline','data','config','tests','tools')){
 $directory=Join-Path $repo $folder
 if(Test-Path -LiteralPath $directory){foreach($actual in Get-ChildItem -LiteralPath $directory -Recurse -File -Force){
  $relative=[IO.Path]::GetRelativePath($repo,$actual.FullName).Replace('\','/')
  if(!$lockedPaths.ContainsKey($relative)){$problems+='Unlocked input: '+$relative}
 }}
}
foreach($file in $lock.Files){
 $path=[IO.Path]::GetFullPath((Join-Path $repo $file.Path))
 if(!$path.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Locked file escaped repository.'}
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){$problems+='Missing: '+$file.Path;continue}
 if((Get-FileHash -LiteralPath $path).Hash -cne $file.SHA256){$problems+='Changed: '+$file.Path}
}
if($problems.Count){throw ($problems -join [Environment]::NewLine)}
$count=0
if($Build){
 if(!$CecilPath){throw 'Supply CecilPath for assembly metadata verification.'}
 Add-Type -LiteralPath $CecilPath
 $spec=Get-Content -LiteralPath (Join-Path $repo 'repo.json') -Raw | ConvertFrom-Json
 $buildRecord=Get-Content -LiteralPath (Join-Path $repo 'out/build-manifest.json') -Raw | ConvertFrom-Json
 $resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
 foreach($folder in @((Join-Path $buildRecord.GameRoot 'BepInEx/core'),(Join-Path $buildRecord.GameRoot 'BepInEx/interop'),$buildRecord.RuntimeRoot)){$resolver.AddSearchDirectory($folder)}
 $reader=[Mono.Cecil.ReaderParameters]::new();$reader.AssemblyResolver=$resolver
 if($buildRecord.SourceLockSHA256 -cne (Get-FileHash -LiteralPath (Join-Path $repo 'maintenance.lock.json')).Hash){throw 'Build uses a different source lock.'}
 foreach($item in $buildRecord.Artifacts){
   $path=Join-Path $repo $item.Path
   if((Get-FileHash -LiteralPath $path).Hash -cne $item.SHA256){throw ('Built DLL changed: '+$item.Id)}
   $expected=$spec.Modules | Where-Object Id -eq $item.Id
   $asm=[Mono.Cecil.ModuleDefinition]::ReadModule($path,$reader)
   try{
     if($asm.Assembly.Name.Name -cne $expected.Assembly){throw ('Wrong assembly: '+$item.Id)}
     $plugins=@($asm.GetTypes().CustomAttributes | Where-Object {$_.AttributeType.Name -eq 'BepInPlugin'})
     if($expected.Kind -ne 'contract'){
       if($plugins.Count -ne 1 -or $plugins[0].ConstructorArguments[0].Value -cne $expected.Guid -or $plugins[0].ConstructorArguments[2].Value -cne $expected.Version){throw ('Wrong plugin identity: '+$item.Id)}
       $deps=@($asm.GetTypes().CustomAttributes | Where-Object {$_.AttributeType.Name -eq 'BepInDependency'} | ForEach-Object {$_.ConstructorArguments[0].Value} | Sort-Object -Unique)
       if((@($expected.DependencyGuids | Sort-Object -Unique) -join '|') -cne ($deps -join '|')){throw ('Plugin dependency identities changed: '+$item.Id)}
       if(@($asm.AssemblyReferences | Where-Object {$_.Name -match '^(WangLocal\.|Migration\.helper)'}).Count){throw ('Unexpected old/helper assembly dependency: '+$item.Id)}
     }elseif($plugins.Count){throw 'Contract library must not contain a plugin.'}
     $count++
   }finally{$asm.Dispose()}
 }
 if($count -ne @($spec.Modules | Where-Object Kind -ne 'data').Count){throw 'Incomplete build artifact list.'}
 $resolver.Dispose()
}
[ordered]@{Repository=$lock.Repository;SourceFilesVerified=@($lock.Files).Count;BuiltAssembliesVerified=$count;Passed=$true;GameplayTested=$false;LegacyDeletionAllowed=$false} | ConvertTo-Json
