# ============================================================================
#  MultiMonitorPlatform  –  Service installer / management script
#  Run as Administrator
# ============================================================================

param(
    [ValidateSet("install","uninstall","start","stop","status")]
    [string]$Action = "status"
)

$ServiceName    = "MultiMonitorPlatform"
$DisplayName    = "Multi-Monitor Platform"
$Description    = "Manages multi-monitor layout, profiles, wallpapers and automation rules."
$BinPath        = Join-Path $PSScriptRoot "MultiMonitorPlatform.exe"

function Require-Admin {
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
              ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Error "This script must be run as Administrator."
        exit 1
    }
}

switch ($Action) {

    "install" {
        Require-Admin
        if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
            Write-Host "Service '$ServiceName' already exists. Use -Action uninstall first." -ForegroundColor Yellow
            exit 0
        }
        New-Service `
            -Name        $ServiceName `
            -DisplayName $DisplayName `
            -Description $Description `
            -BinaryPathName "`"$BinPath`"" `
            -StartupType Automatic
        Write-Host "Service installed. Starting..." -ForegroundColor Green
        Start-Service -Name $ServiceName
        Write-Host "Service started." -ForegroundColor Green
    }

    "uninstall" {
        Require-Admin
        $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if (-not $svc) { Write-Host "Service not found."; exit 0 }
        Stop-Service  -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Remove-Service -Name $ServiceName
        Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
    }

    "start" {
        Require-Admin
        Start-Service -Name $ServiceName
        Write-Host "Service started." -ForegroundColor Green
    }

    "stop" {
        Require-Admin
        Stop-Service -Name $ServiceName -Force
        Write-Host "Service stopped." -ForegroundColor Yellow
    }

    "status" {
        $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($svc) {
            Write-Host "Service: $($svc.DisplayName)  |  Status: $($svc.Status)  |  StartType: $($svc.StartType)"
        } else {
            Write-Host "Service '$ServiceName' is NOT installed." -ForegroundColor Red
        }
    }
}
