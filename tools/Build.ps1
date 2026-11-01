[CmdletBinding()]
param([switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repoRoot 'artifacts'
$app = Join-Path $output 'app'
New-Item -ItemType Directory -Path $output -Force | Out-Null
function Check-Exit([string]$step) { if ($LASTEXITCODE -ne 0) { throw "$step failed ($LASTEXITCODE)" } }
if (-not $SkipChecks) {
    dotnet build (Join-Path $repoRoot 'tests\ProcessFixture\ProcessFixture.csproj') -c Release --nologo
    Check-Exit 'Process fixture build'
    dotnet run --project (Join-Path $repoRoot 'tests\Checks\Checks.csproj') -c Release -- (Join-Path $repoRoot 'tests\ProcessFixture\bin\Release\net10.0')
    Check-Exit 'Core checks'
}
cargo build --release --locked --manifest-path (Join-Path $repoRoot 'connection\Cargo.toml')
Check-Exit 'Phone connection build'
dotnet publish (Join-Path $repoRoot 'src\Gda\Gda.csproj') -c Release -r win-x64 --self-contained true -o $app --nologo
Check-Exit 'Desktop publish'
Copy-Item -LiteralPath (Join-Path $repoRoot 'connection\target\release\gda-phone.exe') -Destination $app
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md'),(Join-Path $repoRoot 'LICENSE'),(Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $app
$zip = Join-Path $output 'GDA-ZZZ-win-x64.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
[IO.Compression.ZipFile]::CreateFromDirectory($app, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
dotnet publish (Join-Path $repoRoot 'src\Setup\Setup.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $output 'setup') --nologo
Check-Exit 'Setup publish'
Copy-Item -LiteralPath (Join-Path $output 'setup\GDA-ZZZ-Setup.exe') -Destination $output
Get-FileHash -LiteralPath (Join-Path $output 'GDA-ZZZ-Setup.exe'),$zip -Algorithm SHA256 | Format-Table Hash,Path
