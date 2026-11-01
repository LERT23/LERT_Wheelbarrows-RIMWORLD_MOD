param([string]$RimWorldPath='C:\GAMES\RimWorld',[switch]$SkipTests)
$ErrorActionPreference='Stop'
$modRoot = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Mod\About\About.xml')) { Join-Path $PSScriptRoot 'Mod' } else { $PSScriptRoot }
if (-not (Test-Path -LiteralPath (Join-Path $RimWorldPath 'RimWorldWin64_Data\Managed\Assembly-CSharp.dll'))) { throw 'Assembly-CSharp.dll not found in RimWorldPath.' }
if (-not $SkipTests) {
    & dotnet test (Join-Path $PSScriptRoot 'Tests\LertWheelbarrows.Tests.csproj') --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
& dotnet build (Join-Path $PSScriptRoot 'Source\LertWheelbarrows.csproj') -c Release "-p:RimWorldPath=$RimWorldPath" "-p:OutputPath=$(Join-Path $modRoot 'Assemblies')" --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $(Join-Path $modRoot 'Assemblies\LertWheelbarrows.dll')"
