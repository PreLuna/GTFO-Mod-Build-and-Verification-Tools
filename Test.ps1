param([Parameter(Mandatory=$true)][string]$RuntimeRoot)
& (Join-Path $PSScriptRoot 'tools/Test-Recipes.ps1') -RepositoryRoot $PSScriptRoot -RuntimeRoot $RuntimeRoot
