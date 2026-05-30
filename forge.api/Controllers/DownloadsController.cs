using System.IO.Compression;
using System.Security.Claims;
using System.Text;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Services;

namespace Forge.Api.Controllers;

/// <summary>
/// Serves client-side tool downloads.
/// The tools/rfid-relay directory is volume-mounted into the container at /app/tools/rfid-relay (read-only).
/// Requires authentication so random internet users cannot enumerate or download files.
/// </summary>
[ApiController]
[Route("api/v1/downloads")]
[Authorize]
[RequiresCapability("CAP-CROSS-DOCS")]
public class DownloadsController : ControllerBase
{
    private readonly string _toolsPath;
    private readonly IDownloadTokenStore _downloadTokens;

    public DownloadsController(IConfiguration config, IDownloadTokenStore downloadTokens)
    {
        _toolsPath = config["ToolsPath"] ?? "/app/tools";
        _downloadTokens = downloadTokens;
    }

    /// <summary>
    /// Returns a self-contained PowerShell setup script for the RFID Relay.
    /// The script embeds the server URL (from the request) and a single-use,
    /// short-lived download token bound to the issuing admin. Running it on a
    /// client machine will: install Node.js if needed, redeem the token at
    /// <c>/rfid-relay-via-token.zip</c>, install dependencies, and register a
    /// Windows Service.
    ///
    /// <para>The embedded credential is deliberately NOT the issuer's Forge
    /// JWT. The PS1 lives on the workstation's filesystem after the install
    /// and is reusable / mailable / backupable; a full JWT at rest there
    /// would be a working bearer credential valid for the full session
    /// lifetime, with the issuer's full grant set. The download token
    /// expires within minutes (see <see cref="DownloadTokenStore"/>), is
    /// single-use, and is scoped to one endpoint.</para>
    /// </summary>
    [HttpGet("rfid-relay-setup.ps1")]
    public IActionResult GetRfidRelaySetupScript()
    {
        // Derive the server base URL from the current request.
        // UseForwardedHeaders middleware applies X-Forwarded-* to Request.Scheme/Host.
        var serverUrl = $"{Request.Scheme}://{Request.Host}";

        // Bind the download token to the issuing admin so the redemption
        // shows up in logs as their action, not as an anonymous fetch.
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var downloadToken = _downloadTokens.Issue(userId);

        var script = GenerateSetupScript(serverUrl, downloadToken);
        // PowerShell 5.1 requires UTF-8 BOM to correctly parse non-ASCII characters
        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(script);
        var bytes = new byte[bom.Length + body.Length];
        bom.CopyTo(bytes, 0);
        body.CopyTo(bytes, bom.Length);
        return File(bytes, "application/octet-stream", "rfid-relay-setup.ps1");
    }

    /// <summary>
    /// Token-gated companion to <see cref="GetRfidRelayZip"/>. The
    /// installer PS1 hits this endpoint with the single-use download token
    /// it received at issuance time; on success the token is consumed and
    /// the zip is returned. Wrong / expired / already-consumed token → 401.
    ///
    /// <para>Anonymous because the credential IS the token in the header
    /// (the issuing JWT is never embedded in the PS1). The token gates this
    /// endpoint as tightly as a JWT would gate the legacy
    /// <see cref="GetRfidRelayZip"/> path.</para>
    /// </summary>
    [HttpGet("rfid-relay-via-token.zip")]
    [AllowAnonymous]
    public IActionResult GetRfidRelayZipViaToken(
        [FromHeader(Name = "X-Forge-Download-Token")] string? token)
    {
        if (string.IsNullOrEmpty(token) || _downloadTokens.Consume(token) is null)
            return Unauthorized();
        return BuildRfidRelayZip();
    }

    /// <summary>
    /// Returns the RFID Relay scripts as a zip file. JWT-gated path retained
    /// for direct admin use (e.g. download from the browser). The PS1
    /// installer uses <see cref="GetRfidRelayZipViaToken"/> instead so it
    /// doesn't have to embed a JWT.
    /// </summary>
    [HttpGet("rfid-relay.zip")]
    public IActionResult GetRfidRelayZip() => BuildRfidRelayZip();

    /// <summary>
    /// Shared zip-building logic used by both the JWT-gated and the
    /// token-gated routes. Reads the same set of relay files in both paths
    /// so the installer experience is identical regardless of auth method.
    /// </summary>
    private IActionResult BuildRfidRelayZip()
    {
        var relayDir = Path.Combine(_toolsPath, "rfid-relay");

        string[] relayFiles =
        [
            "relay.js",
            "list-devices.js",
            "package.json",
            "svc-install.js",
            "svc-uninstall.js",
            "install-service.ps1",
            "uninstall-service.ps1",
        ];

        var foundFiles = relayFiles
            .Select(f => new { Name = f, FullPath = Path.Combine(relayDir, f) })
            .Where(f => System.IO.File.Exists(f.FullPath))
            .ToList();

        if (foundFiles.Count == 0)
            return NotFound(new { message = "RFID relay scripts not found on the server." });

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in foundFiles)
            {
                var entry = archive.CreateEntry($"rfid-relay/{file.Name}", CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = System.IO.File.OpenRead(file.FullPath);
                fileStream.CopyTo(entryStream);
            }
        }

        stream.Position = 0;
        return File(stream, "application/zip", "rfid-relay.zip");
    }

    private static string GenerateSetupScript(string serverUrl, string downloadToken)
    {
        return $$"""
            #Requires -RunAsAdministrator
            <#
            .SYNOPSIS
                Installs the QB Engineer RFID Relay on this workstation.

            .DESCRIPTION
                Downloads and configures the RFID relay service that bridges USB NFC/RFID
                smart card readers to the QB Engineer browser app. This script:
                  1. Installs Node.js (LTS) if not already present
                  2. Downloads relay scripts from the QB Engineer server
                  3. Installs npm dependencies
                  4. Registers a Windows Service that starts automatically at boot

                Run this script as Administrator on each workstation with an RFID reader.

            .PARAMETER InstallDir
                Where to install the relay. Default: C:\Program Files\QB Engineer\RfidRelay

            .PARAMETER Port
                WebSocket port for the relay. Default: 9876

            .EXAMPLE
                .\rfid-relay-setup.ps1
                .\rfid-relay-setup.ps1 -InstallDir "D:\RfidRelay" -Port 9877
            #>
            param(
                [string] $InstallDir = "C:\Program Files\QB Engineer\RfidRelay",
                [int]    $Port       = 9876,
                [int]    $DebounceMs = 500
            )

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'

            $ServerUrl     = "{{serverUrl}}"
            # Single-use, short-lived download token bound to the issuing admin.
            # NOT a full session JWT — see DownloadsController doc comment for why.
            $DownloadToken = "{{downloadToken}}"
            # node-windows derives the service ID from the name: lowercase + stripped + .exe
            $ServiceName = "forgerfidrelay.exe"

            function Write-Step([string]$msg) { Write-Host "`n>> $msg" -ForegroundColor Cyan }
            function Write-OK([string]$msg)   { Write-Host "   OK  $msg" -ForegroundColor Green }
            function Write-Fail([string]$msg) { Write-Host "   ERR $msg" -ForegroundColor Red }

            Write-Host ""
            Write-Host "============================================" -ForegroundColor Cyan
            Write-Host "  QB Engineer RFID Relay -- Client Setup" -ForegroundColor Cyan
            Write-Host "============================================" -ForegroundColor Cyan
            Write-Host "  Server:  $ServerUrl"
            Write-Host "  Install: $InstallDir"
            Write-Host "  Port:    $Port"

            # -- 1. Check / Install Node.js ----------------------------------------

            Write-Step "Checking for Node.js..."

            $nodePath = Get-Command node -ErrorAction SilentlyContinue
            if ($nodePath) {
                $nodeVersion = & node --version 2>&1
                Write-OK "Node.js $nodeVersion found."
            } else {
                Write-Host "   Node.js not found. Installing via winget..." -ForegroundColor Yellow

                $winget = Get-Command winget -ErrorAction SilentlyContinue
                if (-not $winget) {
                    Write-Fail "winget not available. Please install Node.js manually from https://nodejs.org"
                    Write-Host "   After installing Node.js, run this script again." -ForegroundColor Yellow
                    exit 1
                }

                winget install --id OpenJS.NodeJS.LTS --accept-package-agreements --accept-source-agreements
                if ($LASTEXITCODE -ne 0) {
                    Write-Fail "Node.js installation failed (exit $LASTEXITCODE)."
                    Write-Host "   Install manually from https://nodejs.org and run this script again." -ForegroundColor Yellow
                    exit 1
                }

                # Refresh PATH so node/npm are available in this session
                $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

                $nodePath = Get-Command node -ErrorAction SilentlyContinue
                if (-not $nodePath) {
                    Write-Fail "Node.js installed but not found in PATH. Close and reopen PowerShell, then run this script again."
                    exit 1
                }
                Write-OK "Node.js $(& node --version) installed."
            }

            # -- 2. Remove existing service if present --------------------------------

            $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
            if ($existing) {
                Write-Step "Removing existing service..."
                if ($existing.Status -eq 'Running') {
                    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
                    Start-Sleep -Seconds 2
                }
                # Try node-windows uninstall first, fall back to sc.exe
                if (Test-Path (Join-Path $InstallDir "svc-uninstall.js")) {
                    Push-Location $InstallDir
                    try { & node svc-uninstall.js 2>&1 | Out-Null } catch {}
                    Pop-Location
                    Start-Sleep -Seconds 2
                }
                # Verify removal -- if still registered, force via sc.exe
                $still = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
                if ($still) {
                    & sc.exe delete $ServiceName 2>&1 | Out-Null
                    Start-Sleep -Seconds 2
                }
                Write-OK "Previous service removed."
            }

            # Clean up daemon directory (node-windows creates SYSTEM-owned files)
            $daemonDir = Join-Path $InstallDir "daemon"
            if (Test-Path $daemonDir) {
                & takeown /F $daemonDir /R /D Y 2>&1 | Out-Null
                & icacls $daemonDir /grant Administrators:F /T 2>&1 | Out-Null
                Remove-Item $daemonDir -Recurse -Force -ErrorAction SilentlyContinue
                if (Test-Path $daemonDir) {
                    Write-Host "   WARN  Could not fully remove daemon directory. Continuing anyway." -ForegroundColor Yellow
                }
            }

            # -- 3. Download relay scripts -------------------------------------------

            Write-Step "Downloading relay scripts from server..."

            # Token-gated endpoint -- the PS1 carries a single-use download
            # token, not a session JWT. See DownloadsController for rationale.
            $zipUrl  = "$ServerUrl/api/v1/downloads/rfid-relay-via-token.zip"
            $zipPath = Join-Path $env:TEMP "rfid-relay.zip"

            try {
                $headers = @{ 'X-Forge-Download-Token' = $DownloadToken }
                Invoke-WebRequest -Uri $zipUrl -Headers $headers -OutFile $zipPath -UseBasicParsing
                Write-OK "Downloaded relay scripts."
            } catch {
                Write-Fail "Failed to download from $zipUrl"
                Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
                Write-Host "   The download token expires within minutes and is single-use." -ForegroundColor Yellow
                Write-Host "   Download a fresh setup script from the admin panel and re-run it." -ForegroundColor Yellow
                exit 1
            }

            # -- 4. Extract to install directory -------------------------------------

            Write-Step "Extracting to $InstallDir..."

            if (Test-Path $InstallDir) {
                # Remove old files (except node_modules)
                Get-ChildItem $InstallDir -Exclude "node_modules" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
            } else {
                New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
            }

            # Extract zip (files are inside rfid-relay/ subfolder)
            $tempExtract = Join-Path $env:TEMP "rfid-relay-extract"
            if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force }
            Expand-Archive -Path $zipPath -DestinationPath $tempExtract -Force

            $extractedDir = Join-Path $tempExtract "rfid-relay"
            if (-not (Test-Path $extractedDir)) {
                # Zip might not have subfolder -- use temp root
                $extractedDir = $tempExtract
            }

            Copy-Item -Path "$extractedDir\*" -Destination $InstallDir -Recurse -Force
            Remove-Item $tempExtract -Recurse -Force
            Remove-Item $zipPath -Force

            Write-OK "Files extracted."

            # -- 5. Ensure C++ build tools (needed for native RFID drivers) ----------

            Write-Step "Checking C++ build tools..."

            $hasBuildTools = $false
            $vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
            if (Test-Path $vsWhere) {
                $vsPath = & $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>&1
                if ($vsPath) { $hasBuildTools = $true }
            }
            if (-not $hasBuildTools) {
                # Check for standalone Build Tools
                $btPaths = @(
                    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools",
                    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools",
                    "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools"
                )
                foreach ($p in $btPaths) {
                    if (Test-Path $p) { $hasBuildTools = $true; break }
                }
            }

            if ($hasBuildTools) {
                Write-OK "C++ build tools found."
            } else {
                Write-Host "   C++ build tools not found. Installing via winget..." -ForegroundColor Yellow
                $winget = Get-Command winget -ErrorAction SilentlyContinue
                if ($winget) {
                    & winget install --id Microsoft.VisualStudio.2022.BuildTools --override "--quiet --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended" --accept-package-agreements --accept-source-agreements 2>&1 | Out-Null
                    if ($LASTEXITCODE -eq 0) {
                        Write-OK "C++ build tools installed."
                    } else {
                        Write-Host "   WARN  Build tools install may have failed. Native RFID drivers may not compile." -ForegroundColor Yellow
                        Write-Host "   You can install manually: winget install Microsoft.VisualStudio.2022.BuildTools" -ForegroundColor Yellow
                    }
                } else {
                    Write-Host "   WARN  winget not available. Install Visual Studio Build Tools manually." -ForegroundColor Yellow
                    Write-Host "   https://visualstudio.microsoft.com/visual-cpp-build-tools/" -ForegroundColor Yellow
                    Write-Host "   Select 'Desktop development with C++' workload." -ForegroundColor Yellow
                }
            }

            # -- 6. Install npm dependencies -----------------------------------------

            Write-Step "Installing npm dependencies..."

            $npmPath = Get-Command npm -ErrorAction SilentlyContinue
            if (-not $npmPath) {
                Write-Fail "npm not found in PATH. Ensure Node.js is installed correctly."
                exit 1
            }

            Push-Location $InstallDir
            try {
                & npm install 2>&1 | Out-Null
                Write-OK "Core dependencies installed."

                # Check if nfc-pcsc (native RFID driver) compiled successfully
                $pcscPath = Join-Path $InstallDir "node_modules\nfc-pcsc"
                if (Test-Path $pcscPath) {
                    Write-OK "PC/SC smart card driver ready."
                } else {
                    Write-Host "   WARN  nfc-pcsc did not install (native build may have failed)." -ForegroundColor Yellow
                    Write-Host "   The relay will still work with HID/keyboard-wedge RFID readers." -ForegroundColor Yellow
                    Write-Host "   For PC/SC readers (ACR122U), ensure C++ build tools are installed and re-run." -ForegroundColor Yellow
                }
            } finally {
                Pop-Location
            }

            # -- 7. Register and start Windows Service -------------------------------

            Write-Step "Registering Windows Service..."

            Push-Location $InstallDir
            try {
                & node svc-install.js --port $Port --debounce $DebounceMs 2>&1
                if ($LASTEXITCODE -ne 0) {
                    Write-Fail "Service registration failed."
                    exit 1
                }
                Write-OK "Service registered."
            } catch {
                Write-Fail "Service registration failed: $($_.Exception.Message)"
                exit 1
            } finally {
                Pop-Location
            }

            # -- 8. Ensure Smart Card service is available -------------------------

            Write-Step "Checking Windows Smart Card service..."

            $scardSvc = Get-Service -Name SCardSvr -ErrorAction SilentlyContinue
            if (-not $scardSvc) {
                Write-Host "   WARN  Smart Card service not found -- PC/SC readers may not be detected." -ForegroundColor Yellow
            } elseif ($scardSvc.StartType -eq 'Disabled') {
                Set-Service -Name SCardSvr -StartupType Manual
                Write-OK "Smart Card service re-enabled."
            } else {
                Write-OK "Smart Card service OK ($($scardSvc.StartType))."
            }

            # -- 9. Verify service is running -----------------------------------------

            Write-Step "Verifying service..."

            Start-Sleep -Seconds 5

            $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
            if ($svc -and $svc.Status -eq 'Running') {
                Write-OK "Service is running."
            } elseif ($svc) {
                Write-Host "   Service status: $($svc.Status). Attempting manual start..." -ForegroundColor Yellow
                try {
                    Start-Service -Name $ServiceName -ErrorAction Stop
                    Start-Sleep -Seconds 3
                    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
                    if ($svc -and $svc.Status -eq 'Running') {
                        Write-OK "Service started on retry."
                    } else {
                        Write-Fail "Service did not start (Status: $($svc.Status))."
                        Write-Host "   You can test manually: Push-Location `"$InstallDir`"; npm start; Pop-Location" -ForegroundColor Yellow
                    }
                } catch {
                    Write-Fail "Could not start service: $($_.Exception.Message)"
                    Write-Host "   You can test manually: Push-Location `"$InstallDir`"; npm start; Pop-Location" -ForegroundColor Yellow
                }
            } else {
                Write-Fail "Service not found after registration."
                Write-Host "   You can test manually: Push-Location `"$InstallDir`"; npm start; Pop-Location" -ForegroundColor Yellow
            }

            # -- Done ----------------------------------------------------------------

            Write-Host ""
            Write-Host "============================================" -ForegroundColor Green
            Write-Host "  RFID Relay installed successfully!" -ForegroundColor Green
            Write-Host "============================================" -ForegroundColor Green
            Write-Host "  WebSocket:    ws://localhost:$Port"
            Write-Host "  Service:      $ServiceName"
            Write-Host "  Install dir:  $InstallDir"
            Write-Host ""
            Write-Host "  The relay will start automatically when Windows boots."
            Write-Host "  Plug in your RFID reader and open QB Engineer to test."
            Write-Host ""
            Write-Host "  To check status:     Get-Service $ServiceName"
            Write-Host '  To view logs:        Event Viewer > Windows Logs > Application'
            Write-Host "  To uninstall:        cd `"$InstallDir`"; node svc-uninstall.js"
            Write-Host ""
            """;
    }
}
