# ARC Game — LLM Router setup (Windows / PowerShell)
# Idempotent: safe to re-run. Never overwrites an existing real key in .env.
#
#   powershell -ExecutionPolicy Bypass -File .\setup.ps1            # interactive key prompts
#   powershell -ExecutionPolicy Bypass -File .\setup.ps1 -NoKeys    # skip key prompts
#
# Keys can also be supplied non-interactively by setting them first, e.g.:
#   $env:OPENAI_API_KEY="sk-..."; .\setup.ps1
param([switch]$NoKeys)
$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

Write-Host "==> ARC Game router setup (Windows)"

# ---------------------------------------------------------------------------
# 1. Find a Python >= 3.9
# ---------------------------------------------------------------------------
$py = $null
$candidates = @(
  @{ exe = "py";      args = @("-3.12") },
  @{ exe = "py";      args = @("-3.11") },
  @{ exe = "py";      args = @("-3.10") },
  @{ exe = "py";      args = @("-3.9")  },
  @{ exe = "python";  args = @() },
  @{ exe = "python3"; args = @() }
)
foreach ($c in $candidates) {
  if (Get-Command $c.exe -ErrorAction SilentlyContinue) {
    try {
      $ver = & $c.exe @($c.args + @("-c", "import sys;print('{}.{}'.format(*sys.version_info[:2]))")) 2>$null
    } catch { continue }
    if ($ver -match '^3\.(\d+)$' -and [int]$Matches[1] -ge 9) { $py = $c; break }
  }
}
if (-not $py) { Write-Error "Need Python 3.9+ on PATH (tried py -3.x / python / python3)."; exit 1 }
Write-Host "==> Using $($py.exe) $($py.args) ($(& $py.exe @($py.args + @('--version')) 2>&1))"

# ---------------------------------------------------------------------------
# 2. Virtualenv
# ---------------------------------------------------------------------------
if (-not (Test-Path ".venv")) {
  Write-Host "==> Creating virtualenv .venv"
  & $py.exe @($py.args + @("-m", "venv", ".venv"))
} else {
  Write-Host "==> .venv already exists — reusing"
}
$venvPy = ".\.venv\Scripts\python.exe"

# ---------------------------------------------------------------------------
# 3. Dependencies
# ---------------------------------------------------------------------------
Write-Host "==> Installing dependencies (requirements.txt)"
& $venvPy -m pip install --upgrade pip | Out-Null
& $venvPy -m pip install -r requirements.txt

# ---------------------------------------------------------------------------
# 4. Provider keys -> .env  (loaded by the router via python-dotenv)
# ---------------------------------------------------------------------------
$EnvFile = ".env"
if (-not (Test-Path $EnvFile)) { New-Item -ItemType File -Path $EnvFile | Out-Null }

function Test-EnvReal($key) {
  $line = Get-Content $EnvFile | Where-Object { $_ -match "^\s*$key=" } | Select-Object -Last 1
  if (-not $line) { return $false }
  $v = $line -replace "^\s*$key=", ""
  if ($v -eq "" -or $v -like "PASTE_*" -or $v -like "<*") { return $false }
  return $true
}

function Set-EnvVar($key, $val) {
  $lines = @()
  if (Test-Path $EnvFile) {
    $lines = Get-Content $EnvFile | Where-Object { $_ -notmatch "^\s*$key=" }
  }
  $lines += "$key=$val"
  Set-Content -Path $EnvFile -Value $lines -Encoding ASCII
}

function Read-Secret($prompt) {
  $sec = Read-Host -Prompt $prompt -AsSecureString
  $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
  try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Set-ProviderKey($key, $need, $hint) {
  # (a) already configured -> keep
  if (Test-EnvReal $key) { Write-Host "==> $key already set in .env — keeping it"; return }
  # (b) present in current session env -> use it
  $envval = [Environment]::GetEnvironmentVariable($key)
  if ($envval) { Set-EnvVar $key $envval; Write-Host "==> $key taken from environment and saved to .env"; return }
  # (c) interactive prompt (hidden)
  if ((-not $NoKeys) -and [Environment]::UserInteractive) {
    Write-Host ""
    Write-Host "   $key — $hint"
    $label = if ($need -eq "required") { "$key (required)" } else { "$key (optional, blank to skip)" }
    $val = Read-Secret "   $label"
    if ($val) { Set-EnvVar $key $val; Write-Host "   OK — $key saved to .env"; return }
  }
  # (d) fallback
  if ($need -eq "required") {
    if (-not (Test-EnvReal $key)) { Set-EnvVar $key "PASTE_YOUR_${key}_HERE" }
    Write-Host "==> $key not provided — wrote placeholder to .env (edit before running)"
  } else {
    Write-Host "==> $key skipped (optional)"
  }
}

Write-Host "==> Configuring provider keys in .env"
Set-ProviderKey "OPENAI_API_KEY"    "required" "CMU AI-gateway key (used by all 'openai' provider configs -> https://ai-gateway.andrew.cmu.edu/v1)"
Set-ProviderKey "ANTHROPIC_API_KEY" "optional" "only needed if you run an 'anthropic' provider config"
Write-Host "   (ollama-provider configs need no key — they use a local Ollama server)"

# ---------------------------------------------------------------------------
# 5. config\keys.json (router <-> client auth) — copy from example if missing
# ---------------------------------------------------------------------------
if (-not (Test-Path "config\keys.json")) {
  if (Test-Path "config\keys.example.json") {
    Write-Host "==> Creating config\keys.json from config\keys.example.json"
    Copy-Item "config\keys.example.json" "config\keys.json"
  } else {
    Write-Warning "config\keys.example.json missing — create config\keys.json manually."
  }
} else {
  Write-Host "==> config\keys.json already exists — leaving it untouched"
}

# ---------------------------------------------------------------------------
# 6. Git LFS assets (needed only to build the Unity client)
# ---------------------------------------------------------------------------
if ((Get-Command git -ErrorAction SilentlyContinue) -and (Get-Command git-lfs -ErrorAction SilentlyContinue)) {
  Write-Host "==> git lfs pull"
  try { git lfs pull } catch { Write-Host "   (git lfs pull failed — fine if you only run the router)" }
} else {
  Write-Host "==> git-lfs not detected (skipping; needed only to build the Unity client)"
}

Write-Host ""
Write-Host "Setup complete." -ForegroundColor Green
Write-Host "Start the router:"
Write-Host "   .\.venv\Scripts\python.exe agent_router.py --port 9876 --config-dir config --log-dir logs\sessions --keys-file config\keys.json"
Write-Host "(If .env still shows PASTE_YOUR_..., edit it and put your real key in first.)"
