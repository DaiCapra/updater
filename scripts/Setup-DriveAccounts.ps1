<#
.SYNOPSIS
    Interactively sets up the Google service accounts the launcher uses for Google Drive releases.

.DESCRIPTION
    Walks through, asking before each change:
      1. Installing and signing in to the gcloud CLI.
      2. Enabling the Drive API in the project.
      3. For the uploader and downloader accounts: enabling, restoring or creating them, then writing a
         fresh key to credentials\upload.json / credentials\download.json and optionally deleting old keys.
      4. Sharing the release folder with both accounts (Viewer for downloader, upload rights for uploader).
      5. Writing the folder ID into the game folder's launcher.json and publish.json.

    -Root is the game's config folder (for example ..\updater-erdenstone). It holds credentials\,
    launcher.json and publish.json, and defaults to the current directory.

    Press Enter at any prompt to accept the value shown in [brackets].

.EXAMPLE
    cd C:\Data\Programming\updater-erdenstone; ..\updater\scripts\Setup-DriveAccounts.ps1
.EXAMPLE
    .\scripts\Setup-DriveAccounts.ps1 -Root ..\updater-erdenstone -Project <project-id> -FolderId <folder-id>
#>
[CmdletBinding()]
param(
    [string] $Root = (Get-Location).Path,
    [string] $Project,
    [string] $FolderId
)

$ErrorActionPreference = 'Stop'
if (Get-Variable PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    # gcloud reports "not found" on stderr with a non-zero exit code; we check $LASTEXITCODE ourselves.
    $PSNativeCommandUseErrorActionPreference = $false
}

$root = (Resolve-Path $Root).Path
$credentialsDir = Join-Path $root 'credentials'
$profileFiles = @((Join-Path $root 'launcher.json'), (Join-Path $root 'publish.json'))

$accounts = @(
    [pscustomobject]@{ Name = 'uploader';   KeyFile = Join-Path $credentialsDir 'upload.json';   Purpose = 'publishes builds (Launcher.Publish)' }
    [pscustomobject]@{ Name = 'downloader'; KeyFile = Join-Path $credentialsDir 'download.json'; Purpose = 'lets the launcher read and download builds' }
)

# ---------------------------------------------------------------- prompts

function Write-Step([string] $text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Write-Ok([string] $text)   { Write-Host "   $text" -ForegroundColor Green }
function Write-Warn([string] $text) { Write-Host "   $text" -ForegroundColor Yellow }
function Write-Info([string] $text) { Write-Host "   $text" }

function Read-Value([string] $prompt, [string] $default) {
    $suffix = if ($default) { " [$default]" } else { '' }
    do {
        $value = (Read-Host "   $prompt$suffix").Trim()
        if (-not $value) { $value = $default }
    } while (-not $value)
    return $value
}

function Confirm-Step([string] $question, [bool] $default = $true) {
    $hint = if ($default) { '[Y/n]' } else { '[y/N]' }
    while ($true) {
        $answer = (Read-Host "   $question $hint").Trim().ToLowerInvariant()
        if (-not $answer) { return $default }
        if ($answer -in 'y', 'yes') { return $true }
        if ($answer -in 'n', 'no') { return $false }
    }
}

# ---------------------------------------------------------------- gcloud helpers

function Invoke-GCloud {
    # Runs gcloud and returns stdout; returns $null (and keeps stderr in $script:LastGCloudError) on failure.
    $errorFile = [System.IO.Path]::GetTempFileName()
    # Windows PowerShell 5.1 turns redirected native stderr into errors; keep them non-terminating here.
    $ErrorActionPreference = 'Continue'
    try {
        $output = & gcloud @args 2> $errorFile
        $script:LastGCloudError = (Get-Content $errorFile -Raw)
        if ($LASTEXITCODE -ne 0) { return $null }
        return ($output -join "`n")
    }
    finally {
        Remove-Item $errorFile -ErrorAction SilentlyContinue
    }
}

function Assert-GCloud([string] $what) {
    throw "gcloud failed to $what.`n$script:LastGCloudError"
}

function Read-KeyFile([string] $path) {
    if (-not (Test-Path $path)) { return $null }
    try { return Get-Content $path -Raw | ConvertFrom-Json } catch { return $null }
}

# ---------------------------------------------------------------- 1. gcloud

Write-Step 'Google Cloud CLI'
if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) {
    Write-Warn 'gcloud is not installed.'
    if (-not (Confirm-Step 'Install it with winget now?')) { throw 'gcloud is required. Install it from https://cloud.google.com/sdk/docs/install and run this script again.' }
    winget install --id Google.CloudSDK -e --accept-source-agreements --accept-package-agreements
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) { throw 'gcloud was installed but is not on PATH yet. Open a new terminal and run this script again.' }
}
Write-Ok 'gcloud found.'

$activeAccount = Invoke-GCloud auth list --filter=status:ACTIVE --format='value(account)'
if ($activeAccount) {
    Write-Ok "Signed in as $activeAccount."
    if (-not (Confirm-Step 'Use this Google account?')) { $activeAccount = $null }
}
if (-not $activeAccount) {
    Write-Info 'A browser window opens to sign in. Drive access is requested so the script can share the folder.'
    gcloud auth login --enable-gdrive-access
    if ($LASTEXITCODE -ne 0) { throw 'Sign-in failed.' }
    $activeAccount = Invoke-GCloud auth list --filter=status:ACTIVE --format='value(account)'
    Write-Ok "Signed in as $activeAccount."
}

# ---------------------------------------------------------------- 2. project

Write-Step 'Project'
if (-not $Project) {
    $existingKey = $accounts | ForEach-Object { Read-KeyFile $_.KeyFile } | Where-Object { $_ } | Select-Object -First 1
    $defaultProject = if ($existingKey) { $existingKey.project_id } else { Invoke-GCloud config get-value project }
    $Project = Read-Value 'Google Cloud project ID' $defaultProject
}
if (-not (Invoke-GCloud projects describe $Project --format='value(projectId)')) {
    throw "Can't open project '$Project' as $activeAccount.`n$script:LastGCloudError"
}
Write-Ok "Using project $Project."

$driveApi = Invoke-GCloud services list --enabled --project $Project --filter='config.name=drive.googleapis.com' --format='value(config.name)'
if ($driveApi) {
    Write-Ok 'Drive API is enabled.'
}
elseif (Confirm-Step 'The Drive API is not enabled in this project. Enable it?') {
    if ($null -eq (Invoke-GCloud services enable drive.googleapis.com --project $Project)) { Assert-GCloud 'enable the Drive API' }
    Write-Ok 'Drive API enabled.'
}

# ---------------------------------------------------------------- 3. accounts and keys

foreach ($account in $accounts) {
    $email = "$($account.Name)@$Project.iam.gserviceaccount.com"
    Write-Step "Service account '$($account.Name)' - $($account.Purpose)"

    $described = Invoke-GCloud iam service-accounts describe $email --project $Project --format=json
    if ($described) {
        $info = $described | ConvertFrom-Json
        if ($info.disabled) {
            Write-Warn "$email exists but is disabled."
            if (-not (Confirm-Step 'Enable it?')) { Write-Warn 'Skipped.'; continue }
            if ($null -eq (Invoke-GCloud iam service-accounts enable $email --project $Project)) { Assert-GCloud "enable $email" }
            Write-Ok 'Enabled.'
        }
        else {
            Write-Ok "$email exists and is enabled."
        }
    }
    else {
        Write-Warn "$email doesn't exist (or was deleted)."
        $restored = $false
        $oldKey = Read-KeyFile $account.KeyFile
        if ($oldKey -and $oldKey.client_email -eq $email -and $oldKey.client_id) {
            if (Confirm-Step "Try to restore it from its old ID $($oldKey.client_id)? (only works within 30 days of deletion)") {
                if ($null -ne (Invoke-GCloud iam service-accounts undelete $oldKey.client_id --project $Project)) {
                    $restored = $true
                    Write-Ok 'Restored.'
                }
                else {
                    Write-Warn "Couldn't restore it: $($script:LastGCloudError.Trim())"
                }
            }
        }
        if (-not $restored) {
            if (-not (Confirm-Step "Create $email?")) { Write-Warn 'Skipped.'; continue }
            if ($null -eq (Invoke-GCloud iam service-accounts create $account.Name --project $Project --display-name "Release $($account.Name)")) {
                Assert-GCloud "create $email"
            }
            Write-Ok 'Created.'
        }
    }

    # A key is needed unless the existing file belongs to this account and Google still accepts it.
    $existing = Read-KeyFile $account.KeyFile
    $userKeys = @((Invoke-GCloud iam service-accounts keys list --iam-account $email --project $Project --managed-by=user --format='value(name.basename())') -split "`n" | Where-Object { $_ })
    $keyIsValid = $existing -and $existing.client_email -eq $email -and ($userKeys -contains $existing.private_key_id)
    if ($keyIsValid) {
        Write-Ok "$(Split-Path $account.KeyFile -Leaf) holds a valid key for this account."
        $createKey = Confirm-Step 'Create a new key anyway (rotate)?' $false
    }
    else {
        Write-Warn "$(Split-Path $account.KeyFile -Leaf) is missing or holds a key Google no longer accepts."
        $createKey = Confirm-Step 'Create a new key?'
    }

    if ($createKey) {
        New-Item -ItemType Directory -Force -Path $credentialsDir | Out-Null
        $tempKey = "$($account.KeyFile).new"
        if ($null -eq (Invoke-GCloud iam service-accounts keys create $tempKey --iam-account $email --project $Project)) {
            Remove-Item $tempKey -ErrorAction SilentlyContinue
            if ($script:LastGCloudError -match 'disableServiceAccountKeyCreation') {
                throw "Your organization blocks service-account keys (constraints/iam.disableServiceAccountKeyCreation). An org admin must allow key creation for project $Project."
            }
            Assert-GCloud "create a key for $email"
        }
        Move-Item $tempKey $account.KeyFile -Force
        Write-Ok "New key written to $($account.KeyFile)."

        $newKeyId = (Read-KeyFile $account.KeyFile).private_key_id
        $oldKeys = @($userKeys | Where-Object { $_ -ne $newKeyId })
        if ($oldKeys.Count -gt 0 -and (Confirm-Step "Delete the $($oldKeys.Count) older key(s) for this account? Anything still using them stops working.")) {
            foreach ($keyId in $oldKeys) {
                if ($null -eq (Invoke-GCloud iam service-accounts keys delete $keyId --iam-account $email --project $Project --quiet)) {
                    Write-Warn "Couldn't delete key ${keyId}: $($script:LastGCloudError.Trim())"
                }
                else {
                    Write-Ok "Deleted key $keyId."
                }
            }
        }
    }
}

# ---------------------------------------------------------------- 4. share the folder

Write-Step 'Release folder'
if (-not $FolderId) {
    $defaultFolder = $null
    if (Test-Path $profileFiles[1]) {
        $current = (Get-Content $profileFiles[1] -Raw | ConvertFrom-Json).folderId
        if ($current -and $current -notlike 'REPLACE_*') { $defaultFolder = $current }
    }
    $FolderId = Read-Value 'Folder ID or URL (https://drive.google.com/drive/folders/<id>)' $defaultFolder
}
if ($FolderId -match '/folders/([A-Za-z0-9_-]+)') { $FolderId = $Matches[1] }
if ($FolderId -notmatch '^[A-Za-z0-9_-]+$') { throw "'$FolderId' isn't a Drive folder ID." }

if (Confirm-Step 'Share the folder with the service accounts now?') {
    $token = Invoke-GCloud auth print-access-token
    if (-not $token) { Assert-GCloud 'get an access token' }
    $headers = @{ Authorization = "Bearer $token" }

    function Invoke-Drive([string] $method, [string] $uri, $body) {
        try {
            $arguments = @{ Method = $method; Uri = $uri; Headers = $headers; ContentType = 'application/json' }
            if ($body) { $arguments.Body = ($body | ConvertTo-Json) }
            return Invoke-RestMethod @arguments
        }
        catch {
            $detail = $_.ErrorDetails.Message
            if ($detail) { try { $detail = ($detail | ConvertFrom-Json).error.message } catch { } }
            if (-not $detail) { $detail = $_.Exception.Message }
            if ($detail -match 'insufficient.*scope|ACCESS_TOKEN_SCOPE_INSUFFICIENT') {
                throw "Your gcloud sign-in has no Drive access. Run 'gcloud auth login --enable-gdrive-access' and run this script again, or share the folder in the browser."
            }
            throw $detail
        }
    }

    $folder = Invoke-Drive Get "https://www.googleapis.com/drive/v3/files/${FolderId}?fields=name,mimeType,driveId&supportsAllDrives=true"
    if ($folder.mimeType -ne 'application/vnd.google-apps.folder') { throw "$FolderId is a file ($($folder.name)), not a folder." }
    $inSharedDrive = [bool] $folder.driveId
    Write-Ok "Folder '$($folder.name)' found $(if ($inSharedDrive) { 'in a shared drive' } else { 'in My Drive' })."
    if (-not $inSharedDrive) {
        Write-Warn 'Service accounts have no storage quota, so uploads into My Drive fail with storageQuotaExceeded.'
        Write-Warn 'Downloading works. To publish, move the folder into a shared drive and run this script again.'
    }

    # On shared drives, "fileOrganizer" (Content manager) is needed to replace or delete files; My Drive uses "writer" (Editor).
    $grants = @(
        @{ Name = 'downloader'; Role = 'reader'; Label = 'Viewer' }
        @{ Name = 'uploader'; Role = $(if ($inSharedDrive) { 'fileOrganizer' } else { 'writer' }); Label = $(if ($inSharedDrive) { 'Content manager' } else { 'Editor' }) }
    )
    foreach ($grant in $grants) {
        $email = "$($grant.Name)@$Project.iam.gserviceaccount.com"
        $uri = "https://www.googleapis.com/drive/v3/files/$FolderId/permissions?supportsAllDrives=true&sendNotificationEmail=false"
        # Drive can take a minute to recognize a new or restored service account.
        for ($attempt = 1; ; $attempt++) {
            try {
                Invoke-Drive Post $uri @{ type = 'user'; role = $grant.Role; emailAddress = $email } | Out-Null
                Write-Ok "Shared with $email as $($grant.Label)."
                break
            }
            catch {
                $message = $_.Exception.Message
                if ($attempt -ge 6 -or $message -notmatch 'not.*Google Account|invalid.*sharing|Bad Request') { Write-Warn "Couldn't share with ${email}: $message"; break }
                Write-Info "Drive doesn't recognize $email yet; retrying in 20 seconds ($attempt/5)..."
                Start-Sleep -Seconds 20
            }
        }
    }
}
else {
    Write-Info "Share it in the browser: downloader@$Project.iam.gserviceaccount.com as Viewer,"
    Write-Info "uploader@$Project.iam.gserviceaccount.com as Content manager (shared drive) or Editor. Untick 'Notify people'."
}

# ---------------------------------------------------------------- 5. profiles

Write-Step 'Profiles'
foreach ($file in $profileFiles) {
    if (-not (Test-Path $file)) { Write-Warn "$file not found; skipped."; continue }
    $text = Get-Content $file -Raw
    $updated = [regex]::Replace($text, '("folderId"\s*:\s*")[^"]*(")', "`${1}$FolderId`${2}")
    if ($updated -ne $text) {
        [System.IO.File]::WriteAllText($file, $updated)
        Write-Ok "Updated folderId in $(Split-Path $file -Leaf)."
    }
    else {
        Write-Ok "$(Split-Path $file -Leaf) already uses this folder."
    }
}

Write-Step 'Done'
Write-Info 'Try a dry run (lists the folder with the uploader key):'
Write-Info "  dotnet run --project $(Join-Path $PSScriptRoot '..\src\Launcher.Publish') -- --target $(Join-Path $root 'publish.json') --file build-0.0.2.zip --dry-run"
