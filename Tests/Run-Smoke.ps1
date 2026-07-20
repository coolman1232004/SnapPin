param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $PSScriptRoot 'RecognitionSmoke\SnapPin.RecognitionSmoke.csproj'

& (Join-Path $PSScriptRoot 'Test-LocalizationResources.ps1')

dotnet build $project -c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'Smoke-test build failed.' }

$outputRoot = Join-Path $PSScriptRoot "RecognitionSmoke\bin\x64\$Configuration"
$runner = Get-ChildItem -LiteralPath $outputRoot -Recurse -Filter 'SnapPin.RecognitionSmoke.dll' -File |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if (-not $runner) { throw "Smoke-test runner was not found under $outputRoot" }

dotnet $runner.FullName
if ($LASTEXITCODE -ne 0) { throw "Smoke-test suite failed with exit code $LASTEXITCODE." }
