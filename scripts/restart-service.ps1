param([string]$ServiceName = "LevelDBNet8")

Restart-Service -Name $ServiceName -Force
