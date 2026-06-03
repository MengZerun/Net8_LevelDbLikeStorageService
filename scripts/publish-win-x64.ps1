$root = Split-Path -Parent $PSScriptRoot
$publishDir = "$root\publish\win-x64"

dotnet publish "$root\src\StorageService.Api\StorageService.Api.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -o "$publishDir"

Copy-Item "$root\README.md" "$publishDir\README.md" -Force
Copy-Item "$root\scripts\install-service.ps1" "$publishDir\install-service.ps1" -Force
Copy-Item "$root\scripts\uninstall-service.ps1" "$publishDir\uninstall-service.ps1" -Force
Copy-Item "$root\scripts\restart-service.ps1" "$publishDir\restart-service.ps1" -Force

$oldScriptsDir = Join-Path $publishDir "scripts"
if (Test-Path -LiteralPath $oldScriptsDir) {
    Remove-Item -LiteralPath $oldScriptsDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path "$publishDir\log" | Out-Null
