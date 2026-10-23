param([Parameter(Mandatory)][string[]]$Sources,[Parameter(Mandatory)][string[]]$References,[Parameter(Mandatory)][string]$Output,[string[]]$Resources=@(),[switch]$Executable,[switch]$NullableEnabled,[switch]$WarningsAsErrors)
$ErrorActionPreference='Stop'
Add-Type -LiteralPath (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -LiteralPath (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$options=[Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp12)
$trees=[System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach($source in $Sources){$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($source),$options,$source))}
$refs=[System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach($reference in ($References | Select-Object -Unique)){$refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($reference))}
$kind=if($Executable){[Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication}else{[Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary}
$settings=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new($kind).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithAllowUnsafe($true)
if($NullableEnabled){$settings=$settings.WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)}
if($WarningsAsErrors){$settings=$settings.WithGeneralDiagnosticOption([Microsoft.CodeAnalysis.ReportDiagnostic]::Error)}
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create([IO.Path]::GetFileNameWithoutExtension($Output),$trees,$refs,$settings)
$assets=[System.Collections.Generic.List[Microsoft.CodeAnalysis.ResourceDescription]]::new()
foreach($resource in $Resources){
    $parts=$resource.Split('|',2);$resourcePath=$parts[0]
    $provider=[Func[IO.Stream]]({[IO.File]::OpenRead($resourcePath)}.GetNewClosure())
    $assets.Add([Microsoft.CodeAnalysis.ResourceDescription]::new($parts[1],$provider,$true))
}
$stream=[IO.File]::Create($Output)
try {$result=$compilation.Emit($stream,$null,$null,$null,$assets)}finally{$stream.Dispose()}
foreach($diagnostic in $result.Diagnostics){if($diagnostic.Severity -ne 'Hidden'){Write-Output $diagnostic.ToString()}}
if(!$result.Success){throw ('Compilation failed: '+$Output)}
Write-Output ('COMPILED '+$Output)
