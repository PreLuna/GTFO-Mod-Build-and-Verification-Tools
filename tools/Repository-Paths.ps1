function Resolve-RepositoryPath([string]$RepositoryRoot,[string]$RelativePath){
 if([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)){throw 'A nonempty repository-relative path is required.'}
 $root=[IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
 $path=[IO.Path]::GetFullPath((Join-Path $root $RelativePath))
 if(!$path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw ('Path escaped repository: '+$RelativePath)}
 $ancestor=$path
 while($ancestor -ne $root){
  if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw ('Linked input/output paths are not supported: '+$RelativePath)}
  $ancestor=Split-Path $ancestor -Parent
 }
 return $path
}
