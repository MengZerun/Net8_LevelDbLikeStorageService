param([string]$ServiceName = "LevelDBNet8")

try {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
        Write-Host "Service '$ServiceName' removed."
    }
    else {
        Write-Host "Service '$ServiceName' was not found."
    }

    Get-NetFirewallRule -DisplayName "$ServiceName-*" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
}
finally {
    Read-Host "Press Enter to exit"
}
