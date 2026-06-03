param(
    [string]$ServiceName = "LevelDBNet8",
    [string]$DisplayName = "LevelDBNet8",
    [string]$ExePath = "",
    [string]$Config = ""
)

try {
    if ([string]::IsNullOrWhiteSpace($ExePath)) {
        $ExePath = Join-Path $PSScriptRoot "StorageService.Api.exe"
    }

    if (-not (Test-Path -LiteralPath $ExePath)) {
        $fallbackExe = Join-Path (Split-Path -Parent $PSScriptRoot) "publish\win-x64\StorageService.Api.exe"
        if (Test-Path -LiteralPath $fallbackExe) {
            $ExePath = $fallbackExe
        }
    }

    if (-not (Test-Path -LiteralPath $ExePath)) {
        throw "StorageService.Api.exe not found. Expected next to this script or pass -ExePath."
    }

    if ([string]::IsNullOrWhiteSpace($Config)) {
        $Config = Join-Path (Split-Path -Parent $ExePath) "config\config.json"
    }
    elseif (-not [System.IO.Path]::IsPathRooted($Config)) {
        $Config = Join-Path (Split-Path -Parent $ExePath) $Config
    }

    if (-not (Test-Path -LiteralPath $Config)) {
        throw "Config file not found: $Config"
    }

    New-Item -ItemType Directory -Force -Path (Join-Path (Split-Path -Parent $ExePath) "log") | Out-Null

    $binaryPath = "`"$ExePath`" -config `"$Config`""
    New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName $DisplayName -Description "Deepinspection compatible database service implemented by .NET 8" -StartupType Automatic

    foreach ($port in 9877, 9200, 9201, 9202, 9203) {
        $ruleName = "$ServiceName-$port"
        if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $port | Out-Null
        }
    }

    Start-Service -Name $ServiceName
    Write-Host "Service '$ServiceName' installed and started."
}
finally {
    Read-Host "Press Enter to exit"
}
