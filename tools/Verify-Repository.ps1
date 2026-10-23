param([Parameter(Mandatory=$true)][string]$RepositoryRoot,[switch]$Build,[string]$CecilPath='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Repository-Paths.ps1')
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
$spec=Get-Content -LiteralPath (Join-Path $repo 'repo.json') -Raw | ConvertFrom-Json
if($spec.SchemaVersion -ne 3 -or !($spec.PSObject.Properties.Name -contains 'InputRoots')){throw 'Explicit module/input paths (repo schema 3) are required.'}
$lock=Get-Content -LiteralPath (Join-Path $repo 'maintenance.lock.json') -Raw | ConvertFrom-Json
$problems=@()
$lockedPaths=@{};foreach($file in $lock.Files){if($lockedPaths.ContainsKey($file.Path)){throw 'Duplicate locked file.'};$lockedPaths[$file.Path]=$true}
foreach($folder in $spec.InputRoots){
 $directory=Resolve-RepositoryPath $repo $folder
 if(Test-Path -LiteralPath $directory){foreach($actual in Get-ChildItem -LiteralPath $directory -Recurse -File -Force){
  $relative=[IO.Path]::GetRelativePath($repo,$actual.FullName).Replace('\','/')
  if(!$lockedPaths.ContainsKey($relative)){$problems+='Unlocked input: '+$relative}
 }}
}
foreach($file in $lock.Files){
 $path=Resolve-RepositoryPath $repo $file.Path
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){$problems+='Missing: '+$file.Path;continue}
 if((Get-FileHash -LiteralPath $path).Hash -cne $file.SHA256){$problems+='Changed: '+$file.Path}
}
if($problems.Count){throw ($problems -join [Environment]::NewLine)}
foreach($module in $spec.Modules){
 $modulePath=Resolve-RepositoryPath $repo $module.ModuleRoot
 if(!(Test-Path -LiteralPath $modulePath -PathType Container)){throw ('Missing module root: '+$module.Id)}
 foreach($property in @('SourceRoot','DataRoot','ConfigRoot','TestsRoot')){
  if($module.$property){
   $registered=Resolve-RepositoryPath $repo $module.$property
   if(!$registered.StartsWith($modulePath+'\',[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $registered -PathType Container)){throw ('Invalid '+$property+' for '+$module.Id)}
  }
 }
}
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
   $path=Resolve-RepositoryPath $repo $item.Path
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
       $actualDetails=@($asm.GetTypes().CustomAttributes | Where-Object {$_.AttributeType.Name -eq 'BepInDependency'})
       if($actualDetails.Count -ne @($expected.DependencyDetails).Count){throw ('Dependency details incomplete: '+$item.Id)}
       foreach($declared in $expected.DependencyDetails){
        $match=@($actualDetails|Where-Object {$_.ConstructorArguments[0].Value -ceq $declared.Guid})
        if($match.Count -ne 1){throw ('Duplicate/missing dependency: '+$declared.Guid)}
        $flags=1;$version=$null
        foreach($argument in @($match[0].ConstructorArguments|Select-Object -Skip 1)){
         if($argument.Type.FullName -eq 'BepInEx.BepInDependency/DependencyFlags'){$flags=[int]$argument.Value}
         elseif($argument.Type.FullName -eq 'System.String'){$version=[string]$argument.Value}
        }
        if($flags -ne [int]$declared.DependencyFlags -or [string]$version -cne [string]$declared.DeclaredVersion){throw ('Dependency flags/version changed: '+$item.Id+'/'+$declared.Guid)}
       }
       if(@($asm.AssemblyReferences | Where-Object {$_.Name -match '^(WangLocal\.|Migration\.helper)'}).Count){throw ('Unexpected old/helper assembly dependency: '+$item.Id)}
     }elseif($plugins.Count){throw 'Contract library must not contain a plugin.'}
     $count++
   }finally{$asm.Dispose()}
 }
 if($count -ne @($spec.Modules | Where-Object Kind -ne 'data').Count){throw 'Incomplete build artifact list.'}
 $resolver.Dispose()
}
[ordered]@{Repository=$lock.Repository;SourceFilesVerified=@($lock.Files).Count;BuiltAssembliesVerified=$count;Passed=$true;GameplayTested=$false;LegacyDeletionAllowed=$false} | ConvertTo-Json
