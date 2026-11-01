param(
    [string]$ProjectRoot
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:ToolRoot = $PSScriptRoot
$script:ImagesRoot = Join-Path $script:ToolRoot 'images'
$script:StartupErrorLog = Join-Path $script:ToolRoot 'startup_error.log'
$script:ResolvedProjectRoot = $null
$script:CurrentState = $null
$script:RemoteAvailable = $false
$script:RemoteGuardUnsafe = $false
$script:RemoteGuardReason = ''
$script:RemoteSha = $null
$script:LiveProcess = $null
$script:ActiveProcessKind = ''
$script:PendingBackup = $false
$script:BackupQueue = $null
$script:BackupHadMutation = $false
$script:LastBackupSucceeded = $false
$script:UiBusy = $false
$script:LiveBlinkTicks = 0
$script:UiStarted = $false
$script:WorktreeStatusIcon = $null
$script:RemoteStatusIcon = $null
$script:BackupStateLabel = $null
$script:BackupState = 'READY'
$script:Colors = $null

$script:AssetFiles = @{
    Background       = 'wheelbarrows_background.png'
    GameTitle        = 'wheelbarrows_mod_icon.png'
    ToolLogo         = 'github_icon.png'
    WindowIcon       = 'github_backup_logo.ico'
    Backup           = 'backup_icon.png'
    Refresh          = 'refresh_icon.png'
    GitHub           = 'github_icon.png'
    History          = 'history_icon.png'
    ChangedFiles     = 'changed_files_icon.png'
    ClearLog         = 'clear_live_log_icon.png'
    GitConnected     = 'git_connected.png'
    GitDisconnected  = 'git_disconnected.png'
    GitClean         = 'git_clean.png'
    GitChanges       = 'git_changes.png'
    BackupSuccess    = 'backup_success.png'
    BackupError      = 'backup_error.png'
}

function Resolve-ProjectRoot {
    param([string]$RequestedRoot)

    if (-not [string]::IsNullOrWhiteSpace($RequestedRoot)) {
        return [System.IO.Path]::GetFullPath($RequestedRoot)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
}

function Quote-WindowsArgument {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value -or $Value.Length -eq 0) { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $slashes = 0

    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq [char]92) {
            $slashes++
            continue
        }

        if ($character -eq [char]34) {
            for ($i = 0; $i -lt (($slashes * 2) + 1); $i++) { [void]$builder.Append([char]92) }
            [void]$builder.Append([char]34)
            $slashes = 0
            continue
        }

        for ($i = 0; $i -lt $slashes; $i++) { [void]$builder.Append([char]92) }
        $slashes = 0
        [void]$builder.Append($character)
    }

    for ($i = 0; $i -lt ($slashes * 2); $i++) { [void]$builder.Append([char]92) }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Join-ProcessArguments {
    param([string[]]$Arguments)

    $quoted = New-Object System.Collections.Generic.List[string]
    foreach ($argument in @($Arguments)) {
        $quoted.Add((Quote-WindowsArgument ([string]$argument)))
    }
    return [string]::Join(' ', $quoted.ToArray())
}

function Invoke-GitCapture {
    param(
        [string]$ProjectRoot,
        [string[]]$Arguments
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git.exe'
    $psi.Arguments = Join-ProcessArguments (@('-C', $ProjectRoot) + @($Arguments))
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding -ArgumentList $false
    $psi.StandardErrorEncoding = New-Object System.Text.UTF8Encoding -ArgumentList $false

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi

    try {
        if (-not $process.Start()) { throw 'Unable to start git.exe.' }
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        $process.WaitForExit()

        return [pscustomobject]@{
            ExitCode = [int]$process.ExitCode
            StdOut   = [string]$stdout
            StdErr   = [string]$stderr
        }
    }
    finally {
        $process.Dispose()
    }
}

function Convert-GitRemoteToWebUrl {
    param([string]$RemoteUrl)

    if ([string]::IsNullOrWhiteSpace($RemoteUrl)) { return $null }
    $value = $RemoteUrl.Trim()

    if ($value -match '^https://github\.com/([^/]+)/(.+?)(?:\.git)?/?$') {
        $owner = $Matches[1]
        $repo = $Matches[2] -replace '\.git$', ''
        return "https://github.com/$owner/$repo"
    }

    if ($value -match '^git@github\.com:([^/]+)/(.+?)(?:\.git)?$') {
        $owner = $Matches[1]
        $repo = $Matches[2] -replace '\.git$', ''
        return "https://github.com/$owner/$repo"
    }

    return $null
}

function Parse-GitPorcelain {
    param([string]$Text)

    $rows = New-Object System.Collections.Generic.List[object]
    if ([string]::IsNullOrEmpty($Text)) { return @() }

    $normalized = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
    foreach ($line in $normalized.Split([char]10)) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 3) { continue }

        $code = $line.Substring(0, 2)
        $path = $line.Substring(3)
        $kind = 'Changed'

        if ($code -eq '??') { $kind = 'Untracked' }
        elseif ($code.Contains('R')) { $kind = 'Renamed' }
        elseif ($code.Contains('A')) { $kind = 'Added' }
        elseif ($code.Contains('D')) { $kind = 'Deleted' }
        elseif ($code.Contains('M')) { $kind = 'Modified' }

        $rows.Add([pscustomobject]@{
            Code = $code
            Kind = $kind
            Path = $path
            Raw  = $line
        })
    }

    return @($rows.ToArray())
}

function Resolve-GitPath {
    param(
        [string]$ProjectRoot,
        [string]$GitPathName
    )

    $result = Invoke-GitCapture $ProjectRoot @('rev-parse', '--git-path', $GitPathName)
    if ($result.ExitCode -ne 0) { return $null }
    $path = $result.StdOut.Trim()
    if ([string]::IsNullOrWhiteSpace($path)) { return $null }
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $path))
}

function Get-GitOperationState {
    param([string]$ProjectRoot)

    $checks = @(
        @{ Name = 'MERGE_HEAD';      Label = 'merge in progress' },
        @{ Name = 'rebase-merge';    Label = 'rebase in progress' },
        @{ Name = 'rebase-apply';    Label = 'rebase in progress' },
        @{ Name = 'CHERRY_PICK_HEAD'; Label = 'cherry-pick in progress' }
    )

    foreach ($check in $checks) {
        $path = Resolve-GitPath $ProjectRoot $check.Name
        if ($path -and (Test-Path -LiteralPath $path)) {
            return [pscustomobject]@{ Blocked = $true; Reason = $check.Label }
        }
    }

    return [pscustomobject]@{ Blocked = $false; Reason = '' }
}

function Split-Upstream {
    param([string]$Upstream)

    if ([string]::IsNullOrWhiteSpace($Upstream)) {
        return [pscustomobject]@{ Remote = ''; Branch = '' }
    }

    $index = $Upstream.IndexOf('/')
    if ($index -le 0 -or $index -ge ($Upstream.Length - 1)) {
        return [pscustomobject]@{ Remote = ''; Branch = '' }
    }

    return [pscustomobject]@{
        Remote = $Upstream.Substring(0, $index)
        Branch = $Upstream.Substring($index + 1)
    }
}

function Get-SyncStateLabel {
    param([int]$Ahead, [int]$Behind)

    if ($Ahead -gt 0 -and $Behind -gt 0) { return "DIVERGED: +$Ahead / -$Behind" }
    if ($Behind -gt 0) { return "BEHIND: $Behind" }
    if ($Ahead -gt 0) { return "AHEAD: $Ahead" }
    return 'UP TO DATE'
}

function Get-LocalGitState {
    param([string]$ProjectRoot)

    $empty = [pscustomobject]@{
        ProjectOk        = $false
        Branch           = ''
        Detached         = $false
        Upstream         = ''
        UpstreamRemote   = ''
        UpstreamBranch   = ''
        OriginUrl        = ''
        Changes          = @()
        ChangeCount      = 0
        Ahead            = 0
        Behind           = 0
        SyncState        = 'UNKNOWN'
        LastCommit       = ''
        OperationBlocked = $false
        OperationReason  = ''
        HasUnmerged      = $false
        HeadSha          = ''
        UpstreamSha      = ''
    }

    $projectCheck = Invoke-GitCapture $ProjectRoot @('rev-parse', '--is-inside-work-tree')
    if ($projectCheck.ExitCode -ne 0 -or $projectCheck.StdOut.Trim() -ne 'true') { return $empty }
    $empty.ProjectOk = $true

    $branchResult = Invoke-GitCapture $ProjectRoot @('symbolic-ref', '--quiet', '--short', 'HEAD')
    if ($branchResult.ExitCode -eq 0) {
        $empty.Branch = $branchResult.StdOut.Trim()
    }
    else {
        $empty.Detached = $true
        $empty.Branch = 'DETACHED'
    }

    $headResult = Invoke-GitCapture $ProjectRoot @('rev-parse', 'HEAD')
    if ($headResult.ExitCode -eq 0) { $empty.HeadSha = $headResult.StdOut.Trim() }

    $upstreamResult = Invoke-GitCapture $ProjectRoot @('rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{u}')
    if ($upstreamResult.ExitCode -eq 0) {
        $empty.Upstream = $upstreamResult.StdOut.Trim()
        $parts = Split-Upstream $empty.Upstream
        $empty.UpstreamRemote = $parts.Remote
        $empty.UpstreamBranch = $parts.Branch

        $upstreamShaResult = Invoke-GitCapture $ProjectRoot @('rev-parse', '@{u}')
        if ($upstreamShaResult.ExitCode -eq 0) { $empty.UpstreamSha = $upstreamShaResult.StdOut.Trim() }

        $counts = Invoke-GitCapture $ProjectRoot @('rev-list', '--left-right', '--count', 'HEAD...@{u}')
        if ($counts.ExitCode -eq 0 -and $counts.StdOut.Trim() -match '^\s*(\d+)\s+(\d+)\s*$') {
            $empty.Ahead = [int]$Matches[1]
            $empty.Behind = [int]$Matches[2]
            $empty.SyncState = Get-SyncStateLabel $empty.Ahead $empty.Behind
        }
    }

    $origin = Invoke-GitCapture $ProjectRoot @('remote', 'get-url', 'origin')
    if ($origin.ExitCode -eq 0) { $empty.OriginUrl = $origin.StdOut.Trim() }

    $status = Invoke-GitCapture $ProjectRoot @('-c', 'core.quotepath=false', 'status', '--porcelain=v1', '--untracked-files=all')
    if ($status.ExitCode -eq 0) {
        $empty.Changes = @(Parse-GitPorcelain $status.StdOut)
        $empty.ChangeCount = $empty.Changes.Count
        $empty.HasUnmerged = @($empty.Changes | Where-Object { $_.Code -match '^(DD|AU|UD|UA|DU|AA|UU)$' }).Count -gt 0
    }

    $last = Invoke-GitCapture $ProjectRoot @('log', '-1', '--date=format-local:%Y-%m-%d %H:%M', '--pretty=format:%h%x09%ad%x09%s')
    if ($last.ExitCode -eq 0) { $empty.LastCommit = $last.StdOut.Trim() }

    $operation = Get-GitOperationState $ProjectRoot
    $empty.OperationBlocked = [bool]$operation.Blocked
    $empty.OperationReason = [string]$operation.Reason
    return $empty
}

function Load-UiImage {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $source = [System.Drawing.Image]::FromStream($stream)
            try { return New-Object System.Drawing.Bitmap -ArgumentList $source }
            finally { $source.Dispose() }
        }
        finally { $stream.Dispose() }
    }
    catch { return $null }
}

function Load-UiIcon {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try { return New-Object System.Drawing.Icon -ArgumentList $Path }
    catch { return $null }
}

function Get-AssetPath {
    param([string]$Key)
    if (-not $script:AssetFiles.ContainsKey($Key)) { return $null }
    return Join-Path $script:ImagesRoot $script:AssetFiles[$Key]
}

function Initialize-LiveProcessPump {
    if ('LertWheelbarrowsGitLiveProcessPump' -as [type]) { return }

    Add-Type -TypeDefinition @"
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

public sealed class LertWheelbarrowsGitLiveProcessPump : IDisposable
{
    private readonly ConcurrentQueue<string> _chunks = new ConcurrentQueue<string>();
    private readonly object _captureLock = new object();
    private readonly StringBuilder _stdout = new StringBuilder();
    private readonly StringBuilder _stderr = new StringBuilder();
    private Process _process;
    private Thread _stdoutThread;
    private Thread _stderrThread;

    public int Id { get { return _process == null ? 0 : _process.Id; } }
    public bool HasExited
    {
        get
        {
            if (_process == null) return true;
            try { return _process.HasExited; }
            catch { return true; }
        }
    }
    public int ExitCode { get { return _process.ExitCode; } }
    public string StandardOutput { get { lock (_captureLock) { return _stdout.ToString(); } } }
    public string StandardError { get { lock (_captureLock) { return _stderr.ToString(); } } }

    public void Start(string fileName, string arguments, string workingDirectory)
    {
        var utf8 = new UTF8Encoding(false);
        var psi = new ProcessStartInfo();
        psi.FileName = fileName;
        psi.Arguments = arguments;
        psi.WorkingDirectory = workingDirectory;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = utf8;
        psi.StandardErrorEncoding = utf8;

        _process = new Process();
        _process.StartInfo = psi;
        if (!_process.Start()) throw new InvalidOperationException("Unable to start git process.");

        _stdoutThread = new Thread(() => Pump(_process.StandardOutput, false));
        _stderrThread = new Thread(() => Pump(_process.StandardError, true));
        _stdoutThread.IsBackground = true;
        _stderrThread.IsBackground = true;
        _stdoutThread.Start();
        _stderrThread.Start();
    }

    private void Pump(StreamReader reader, bool isError)
    {
        var buffer = new char[512];
        try
        {
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                var chunk = new string(buffer, 0, read);
                lock (_captureLock)
                {
                    if (isError) _stderr.Append(chunk);
                    else _stdout.Append(chunk);
                }
                _chunks.Enqueue(chunk);
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    public bool TryDequeue(out string chunk) { return _chunks.TryDequeue(out chunk); }

    public void WaitForExitAndDrain()
    {
        if (_process == null) return;
        try { _process.WaitForExit(); } catch { }
        try { if (_stdoutThread != null) _stdoutThread.Join(2000); } catch { }
        try { if (_stderrThread != null) _stderrThread.Join(2000); } catch { }
    }

    public void Dispose()
    {
        try { if (_process != null) _process.Dispose(); } catch { }
        _process = null;
    }
}
"@
}

function Get-TerminalLineColor {
    param([string]$Text)

    if ($null -eq $script:Colors) { return $null }
    if ([string]::IsNullOrWhiteSpace($Text)) { return $script:Colors.Text }

    $trimmed = $Text.TrimStart()
    if ($trimmed.StartsWith('> git')) { return $script:Colors.Accent }
    if ($trimmed.StartsWith('SUCCESS:') -or $trimmed.StartsWith('Already up to date.')) { return $script:Colors.Good }
    if ($trimmed.StartsWith('WARNING:') -or $trimmed.StartsWith('warning:')) { return $script:Colors.Warn }
    if ($trimmed.StartsWith('ERROR:') -or $trimmed.StartsWith('BLOCKED:') -or $trimmed.StartsWith('REMOTE UNAVAILABLE:') -or $trimmed.StartsWith('PROCESS MONITOR ERROR:') -or $trimmed.StartsWith('HISTORY ERROR:') -or $trimmed.StartsWith('OPEN GITHUB ERROR:')) { return $script:Colors.Bad }
    return $script:Colors.Text
}

function Append-Terminal {
    param(
        [string]$Text,
        $Color = $null
    )
    if ($null -eq $script:LogBox -or [string]::IsNullOrEmpty($Text)) { return }
    if ($null -eq $Color) { $Color = Get-TerminalLineColor $Text }

    $script:LogBox.SelectionStart = $script:LogBox.TextLength
    $script:LogBox.SelectionLength = 0
    if ($null -ne $Color) { $script:LogBox.SelectionColor = $Color }
    $script:LogBox.AppendText($Text)
    $script:LogBox.SelectionStart = $script:LogBox.TextLength
    $script:LogBox.SelectionLength = 0
    $script:LogBox.SelectionColor = $script:Colors.Text
    $script:LogBox.ScrollToCaret()
}

function Append-TerminalLine {
    param([string]$Text)
    Append-Terminal ($Text + [Environment]::NewLine) (Get-TerminalLineColor $Text)
}

function Set-StatusValue {
    param(
        $Label,
        [string]$Text,
        $Color
    )
    if ($null -eq $Label) { return }
    $Label.Text = $Text
    $Label.ForeColor = $Color
}

function Set-BackupResultImage {
    param([ValidateSet('None','Success','Error')][string]$State)
    if ($null -eq $script:BackupResultPicture) { return }
    $script:BackupResultPicture.Image = $null
    if ($State -eq 'Success') { $script:BackupResultPicture.Image = $script:UiImages['BackupSuccess'] }
    elseif ($State -eq 'Error') { $script:BackupResultPicture.Image = $script:UiImages['BackupError'] }
}

function Set-BackupState {
    param([ValidateSet('READY','RUNNING','SUCCESS','ERROR')][string]$State)

    $script:BackupState = $State
    if ($null -eq $script:BackupStateLabel) { return }

    $script:BackupStateLabel.Text = 'BACKUP: ' + $State
    switch ($State) {
        'READY'   { $script:BackupStateLabel.ForeColor = $script:Colors.Muted }
        'RUNNING' { $script:BackupStateLabel.ForeColor = $script:Colors.Warn }
        'SUCCESS' { $script:BackupStateLabel.ForeColor = $script:Colors.Good }
        'ERROR'   { $script:BackupStateLabel.ForeColor = $script:Colors.Bad }
    }
}

function Update-BackupButtonState {
    if ($null -eq $script:BackupButton) { return }
    if ($script:UiBusy) { $script:BackupButton.Enabled = $false; return }
    if ($null -eq $script:CurrentState) { $script:BackupButton.Enabled = $false; return }

    $state = $script:CurrentState
    $enabled = $state.ProjectOk -and (-not $state.Detached) -and (-not [string]::IsNullOrWhiteSpace($state.Upstream)) -and (-not $state.OperationBlocked) -and (-not $state.HasUnmerged)
    if ($state.Behind -gt 0) { $enabled = $false }
    if ($state.Ahead -gt 0 -and $state.Behind -gt 0) { $enabled = $false }
    if ($script:RemoteGuardUnsafe) { $enabled = $false }
    $script:BackupButton.Enabled = [bool]$enabled
}

function Set-ActionLock {
    param([bool]$Busy)
    $script:UiBusy = $Busy
    if ($null -ne $script:RefreshButton) { $script:RefreshButton.Enabled = -not $Busy }
    if ($null -ne $script:GitHubButton) { $script:GitHubButton.Enabled = -not $Busy }
    if ($null -ne $script:HistoryButton) { $script:HistoryButton.Enabled = -not $Busy }
    Update-BackupButtonState
}

function Refresh-ChangedFilesList {
    param([object[]]$Changes)
    if ($null -eq $script:ChangedList) { return }
    $script:ChangedList.BeginUpdate()
    try {
        $script:ChangedList.Items.Clear()
        foreach ($change in @($Changes)) {
            $item = New-Object System.Windows.Forms.ListViewItem -ArgumentList $change.Code
            [void]$item.SubItems.Add($change.Kind)
            [void]$item.SubItems.Add($change.Path)
            [void]$script:ChangedList.Items.Add($item)
        }
    }
    finally { $script:ChangedList.EndUpdate() }
}

function Set-StatusIcon {
    param($PictureBox, $Image)
    if ($null -eq $PictureBox) { return }
    $PictureBox.Image = $Image
}

function Refresh-LocalUi {
    param([bool]$StartRemote = $true)

    $state = Get-LocalGitState $script:ResolvedProjectRoot
    $script:CurrentState = $state
    $script:RemoteGuardUnsafe = $false
    $script:RemoteGuardReason = ''

    if (-not $state.ProjectOk) {
        Set-StatusValue $script:ProjectValue 'ERROR' $script:Colors.Bad
        Set-StatusValue $script:WorktreeValue 'ERROR' $script:Colors.Bad
        Set-StatusIcon $script:WorktreeStatusIcon $script:UiImages['GitDisconnected']
        Set-StatusIcon $script:RemoteStatusIcon $script:UiImages['GitDisconnected']
        Set-StatusValue $script:RemoteValue 'NOT CONFIGURED' $script:Colors.Muted
        Set-StatusIcon $script:RemoteStatusIcon $script:UiImages['GitDisconnected']
        Set-StatusValue $script:SyncValue 'UNKNOWN' $script:Colors.Muted
        $script:ContextLabel.Text = 'Git repository not found: ' + $script:ResolvedProjectRoot
        Refresh-ChangedFilesList @()
        Update-BackupButtonState
        return
    }

    Set-StatusValue $script:ProjectValue 'FOUND' $script:Colors.Good
    if ($state.ChangeCount -gt 0) {
        Set-StatusValue $script:WorktreeValue ("CHANGES: " + $state.ChangeCount) $script:Colors.Warn
        Set-StatusIcon $script:WorktreeStatusIcon $script:UiImages['GitChanges']
    }
    else {
        Set-StatusValue $script:WorktreeValue 'CLEAN' $script:Colors.Good
        Set-StatusIcon $script:WorktreeStatusIcon $script:UiImages['GitClean']
    }

    if ([string]::IsNullOrWhiteSpace($state.Upstream)) {
        Set-StatusValue $script:RemoteValue 'NOT CONFIGURED' $script:Colors.Muted
        Set-StatusIcon $script:RemoteStatusIcon $script:UiImages['GitDisconnected']
        Set-StatusValue $script:SyncValue 'UNKNOWN' $script:Colors.Muted
    }
    else {
        Set-StatusValue $script:RemoteValue 'CHECKING...' $script:Colors.Warn
        Set-StatusIcon $script:RemoteStatusIcon $null
        Set-StatusValue $script:SyncValue $state.SyncState $(if ($state.SyncState -eq 'UP TO DATE') { $script:Colors.Good } elseif ($state.SyncState -like 'AHEAD*') { $script:Colors.Warn } elseif ($state.SyncState -eq 'UNKNOWN') { $script:Colors.Muted } else { $script:Colors.Bad })
    }

    $operationText = if ($state.OperationBlocked) { ' | BLOCKED: ' + $state.OperationReason } else { '' }
    $script:ContextLabel.Text = "Branch: $($state.Branch)   Upstream: $($state.Upstream)   Last: $($state.LastCommit)$operationText"
    Refresh-ChangedFilesList $state.Changes
    Update-BackupButtonState

    if ($StartRemote -and -not [string]::IsNullOrWhiteSpace($state.Upstream) -and -not $script:UiBusy) {
        Start-RemoteRefresh
    }
}

function Start-LiveGitProcess {
    param(
        [string[]]$Arguments,
        [string]$Kind,
        [bool]$EchoCommand = $true
    )

    if ($null -ne $script:LiveProcess) { throw 'Another Git process is already running.' }
    Initialize-LiveProcessPump

    $full = @('-C', $script:ResolvedProjectRoot) + @($Arguments)
    if ($EchoCommand) { Append-TerminalLine ('> git ' + (Join-ProcessArguments $Arguments)) }

    $pump = New-Object LertWheelbarrowsGitLiveProcessPump
    $pump.Start('git.exe', (Join-ProcessArguments $full), $script:ResolvedProjectRoot)
    $script:LiveProcess = $pump
    $script:ActiveProcessKind = $Kind
    Set-ActionLock $true
}

function Start-RemoteRefresh {
    if ($null -ne $script:LiveProcess) { return }
    if ($null -eq $script:CurrentState -or -not $script:CurrentState.ProjectOk) { return }
    if ([string]::IsNullOrWhiteSpace($script:CurrentState.UpstreamRemote) -or [string]::IsNullOrWhiteSpace($script:CurrentState.UpstreamBranch)) {
        Set-StatusValue $script:RemoteValue 'NOT CONFIGURED' $script:Colors.Muted
        Set-StatusValue $script:SyncValue 'UNKNOWN' $script:Colors.Muted
        return
    }

    $script:RemoteAvailable = $false
    $script:RemoteGuardUnsafe = $false
    $script:RemoteGuardReason = ''
    $script:RemoteSha = $null
    Set-StatusValue $script:RemoteValue 'CHECKING...' $script:Colors.Warn
    Start-LiveGitProcess @('ls-remote', '--exit-code', $script:CurrentState.UpstreamRemote, ('refs/heads/' + $script:CurrentState.UpstreamBranch)) 'RemoteRefresh' $false
}

function Apply-ResolvedSyncState {
    param([int]$Ahead, [int]$Behind)
    $script:CurrentState.Ahead = $Ahead
    $script:CurrentState.Behind = $Behind
    $script:CurrentState.SyncState = Get-SyncStateLabel $Ahead $Behind
    $color = if ($Ahead -eq 0 -and $Behind -eq 0) { $script:Colors.Good } elseif ($Behind -eq 0) { $script:Colors.Warn } else { $script:Colors.Bad }
    Set-StatusValue $script:SyncValue $script:CurrentState.SyncState $color
}

function Complete-RemoteRefresh {
    param(
        [int]$ExitCode,
        [string]$StdOut,
        [string]$StdErr
    )

    $script:RemoteAvailable = $false
    $script:RemoteGuardUnsafe = $false
    $script:RemoteGuardReason = ''

    if ($ExitCode -ne 0) {
        if ($ExitCode -eq 2 -and [string]::IsNullOrWhiteSpace($StdErr)) {
            $script:RemoteAvailable = $true
            $script:RemoteGuardUnsafe = $true
            $script:RemoteGuardReason = 'upstream branch not found on remote'
            Set-StatusValue $script:RemoteValue 'CONNECTED' $script:Colors.Good
            Set-StatusIcon $script:RemoteStatusIcon $script:UiImages['GitConnected']
            Set-StatusValue $script:SyncValue 'UNKNOWN' $script:Colors.Muted
            Append-TerminalLine 'REMOTE: upstream branch not found. Backup is blocked to avoid recreating it automatically.'
            Set-ActionLock $false
            if ($script:PendingBackup) { Continue-BackupAfterRemoteCheck $true }
            return
        }

        Set-StatusValue $script:RemoteValue 'UNAVAILABLE' $script:Colors.Bad
        Set-StatusIcon $script:RemoteStatusIcon $script:UiImages['GitDisconnected']
        Set-StatusValue $script:SyncValue 'UNKNOWN' $script:Colors.Muted
        if (-not [string]::IsNullOrWhiteSpace($StdErr)) { Append-TerminalLine ('REMOTE: ' + $StdErr.Trim()) }
        Set-ActionLock $false
        if ($script:PendingBackup) { Continue-BackupAfterRemoteCheck $false }
        return
    }

    $line = ($StdOut.Replace("`r`n", "`n").Split([char]10) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
    if ($null -eq $line -or $line -notmatch '^([0-9a-fA-F]{40})\s+') {
        Set-StatusValue $script:RemoteValue 'UNAVAILABLE' $script:Colors.Bad
        Set-StatusIcon $script:RemoteStatusIcon $script:UiImages['GitDisconnected']
        Set-StatusValue $script:SyncValue 'UNKNOWN' $script:Colors.Muted
        Append-TerminalLine 'REMOTE: branch SHA could not be read.'
        Set-ActionLock $false
        if ($script:PendingBackup) { Continue-BackupAfterRemoteCheck $false }
        return
    }

    $remoteSha = $Matches[1].ToLowerInvariant()
    $script:RemoteSha = $remoteSha
    $script:RemoteAvailable = $true
    Set-StatusValue $script:RemoteValue 'CONNECTED' $script:Colors.Good
    Set-StatusIcon $script:RemoteStatusIcon $script:UiImages['GitConnected']

    if ($remoteSha -eq $script:CurrentState.UpstreamSha.ToLowerInvariant()) {
        Apply-ResolvedSyncState $script:CurrentState.Ahead $script:CurrentState.Behind
    }
    else {
        $available = Invoke-GitCapture $script:ResolvedProjectRoot @('cat-file', '-e', ($remoteSha + '^{commit}'))
        if ($available.ExitCode -eq 0) {
            $counts = Invoke-GitCapture $script:ResolvedProjectRoot @('rev-list', '--left-right', '--count', ("HEAD...$remoteSha"))
            if ($counts.ExitCode -eq 0 -and $counts.StdOut.Trim() -match '^\s*(\d+)\s+(\d+)\s*$') {
                Apply-ResolvedSyncState ([int]$Matches[1]) ([int]$Matches[2])
            }
            else {
                $script:RemoteGuardUnsafe = $true
                $script:RemoteGuardReason = 'remote state could not be compared safely'
                Set-StatusValue $script:SyncValue 'UNKNOWN' $script:Colors.Muted
            }
        }
        else {
            $script:RemoteGuardUnsafe = $true
            $script:RemoteGuardReason = 'remote branch moved; run manual fetch/integration'
            Set-StatusValue $script:SyncValue 'UNKNOWN' $script:Colors.Muted
            Append-TerminalLine 'REMOTE: branch changed outside local refs. Manual Git integration/fetch is required before Backup.'
        }
    }

    Set-ActionLock $false
    if ($script:PendingBackup) { Continue-BackupAfterRemoteCheck $true }
}

function Get-PublishDestinationIssue {
    param([object]$State)
    if ($null -eq $State -or [string]::IsNullOrWhiteSpace($State.UpstreamRemote)) { return 'No upstream remote is configured.' }
    $result = Invoke-GitCapture $script:ResolvedProjectRoot @('remote', 'get-url', '--push', '--all', $State.UpstreamRemote)
    if ($result.ExitCode -ne 0) { return 'Cannot resolve the effective Git push destination.' }
    $allowed = @(
        'https://github.com/LERT23/LERT_Wheelbarrows-RIMWORLD_MOD.git',
        'https://github.com/LERT23/LERT_Wheelbarrows-RIMWORLD_MOD',
        'git@github.com:LERT23/LERT_Wheelbarrows-RIMWORLD_MOD.git'
    )
    $urls = @($result.StdOut -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($urls.Count -eq 0) { return 'No Git push destination is configured.' }
    foreach ($url in $urls) {
        if ($url.Trim() -notin $allowed) { return 'Push destination is not the configured LERT Wheelbarrows repository. Git settings were not changed.' }
    }
    return $null
}

function Test-BackupPreflight {
    param([object]$State)

    if ($null -eq $State -or -not $State.ProjectOk) { return 'Project is not a valid Git worktree.' }
    if ($State.Detached) { return 'Detached HEAD: Backup is blocked.' }
    if ([string]::IsNullOrWhiteSpace($State.Upstream)) { return 'No upstream is configured. Backup is blocked.' }
    $destinationIssue = Get-PublishDestinationIssue $State
    if ($destinationIssue) { return $destinationIssue }
    if ($State.OperationBlocked) { return ('Git operation in progress: ' + $State.OperationReason + '. Backup is blocked.') }
    if ($State.HasUnmerged) { return 'Unmerged index entries detected. Resolve conflicts manually before Backup.' }
    if ($State.Behind -gt 0 -and $State.Ahead -gt 0) { return 'Branch is diverged. Manual integration is required.' }
    if ($State.Behind -gt 0) { return 'Remote branch is ahead. Manual integration is required.' }
    if ($script:RemoteGuardUnsafe) { return ('Remote safety check blocked Backup: ' + $script:RemoteGuardReason + '.') }
    return $null
}

function New-BackupQueue {
    param([object]$State)

    $queue = New-Object System.Collections.Queue
    if ($State.ChangeCount -gt 0) {
        $timestamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
        $queue.Enqueue([pscustomobject]@{ Name = 'add'; Args = @('add', '-A') })
        $queue.Enqueue([pscustomobject]@{ Name = 'commit'; Args = @('commit', '-m', ("Lert Wheelbarrows backup $timestamp")) })
        $queue.Enqueue([pscustomobject]@{ Name = 'push'; Args = @('push', $State.UpstreamRemote, ('HEAD:' + $State.UpstreamBranch)) })
        return ,$queue
    }

    if ($State.Ahead -gt 0) {
        $queue.Enqueue([pscustomobject]@{ Name = 'push'; Args = @('push', $State.UpstreamRemote, ('HEAD:' + $State.UpstreamBranch)) })
    }
    return ,$queue
}

function Start-NextBackupCommand {
    if ($null -eq $script:BackupQueue -or $script:BackupQueue.Count -eq 0) {
        $script:PendingBackup = $false
        $script:LastBackupSucceeded = $true
        Set-BackupResultImage 'Success'
        Set-BackupState 'SUCCESS'
        Append-TerminalLine 'SUCCESS: LERT WHEELBARROWS backup completed.'
        Set-ActionLock $false
        Refresh-LocalUi $true
        return
    }

    $command = $script:BackupQueue.Dequeue()
    if ($command.Name -eq 'push') {
        $destinationIssue = Get-PublishDestinationIssue $script:CurrentState
        if ($destinationIssue) {
            $script:PendingBackup = $false
            $script:BackupQueue.Clear()
            Set-BackupState 'ERROR'
            Set-BackupResultImage 'Error'
            Append-TerminalLine ('BLOCKED: ' + $destinationIssue + ' Local commits are preserved.')
            Set-ActionLock $false
            return
        }
    }
    if ($command.Name -eq 'add' -or $command.Name -eq 'commit') { $script:BackupHadMutation = $true }
    Start-LiveGitProcess $command.Args ('Backup:' + $command.Name) $true
}

function Continue-BackupAfterRemoteCheck {
    param([bool]$RemoteAvailable)

    $state = $script:CurrentState
    $preflight = Test-BackupPreflight $state
    if ($preflight) {
        $script:PendingBackup = $false
        Set-BackupResultImage 'Error'
        Set-BackupState 'ERROR'
        Append-TerminalLine ('BLOCKED: ' + $preflight)
        Update-BackupButtonState
        return
    }

    if (-not $RemoteAvailable -and $state.ChangeCount -eq 0 -and $state.Ahead -eq 0) {
        $script:PendingBackup = $false
        Set-BackupResultImage 'Error'
        Set-BackupState 'ERROR'
        Append-TerminalLine 'REMOTE UNAVAILABLE: sync is unknown; no safe no-op can be claimed.'
        Update-BackupButtonState
        return
    }

    if ($RemoteAvailable -and $state.ChangeCount -eq 0 -and $state.Ahead -eq 0 -and $state.Behind -eq 0) {
        $script:PendingBackup = $false
        Set-BackupResultImage 'Success'
        Set-BackupState 'SUCCESS'
        Append-TerminalLine 'Already up to date.'
        Update-BackupButtonState
        return
    }

    $script:BackupQueue = New-BackupQueue $state
    if ($script:BackupQueue.Count -eq 0) {
        $script:PendingBackup = $false
        Set-BackupResultImage 'Error'
        Set-BackupState 'ERROR'
        Append-TerminalLine 'Backup has no safe action to perform.'
        Update-BackupButtonState
        return
    }

    $script:BackupHadMutation = $false
    Start-NextBackupCommand
}

function Start-Backup {
    if ($script:UiBusy -or $null -ne $script:LiveProcess) { return }
    Set-BackupResultImage 'None'
    Set-BackupState 'RUNNING'
    Append-TerminalLine ''
    Append-TerminalLine '=== BACKUP START ==='
    Refresh-LocalUi $false

    $basic = Test-BackupPreflight $script:CurrentState
    if ($basic) {
        Set-BackupResultImage 'Error'
        Set-BackupState 'ERROR'
        Append-TerminalLine ('BLOCKED: ' + $basic)
        return
    }

    $script:PendingBackup = $true
    Start-RemoteRefresh
}

function Drain-LiveOutput {
    if ($null -eq $script:LiveProcess) { return }
    try {
        $chunk = $null
        while ($script:LiveProcess.TryDequeue([ref]$chunk)) {
            if (-not [string]::IsNullOrEmpty($chunk) -and $script:ActiveProcessKind -ne 'RemoteRefresh') { Append-Terminal $chunk }
            $chunk = $null
        }
    }
    catch { }
}

function Stop-LiveProcess {
    if ($null -eq $script:LiveProcess) { return }
    try {
        if (-not $script:LiveProcess.HasExited -and $script:LiveProcess.Id -gt 0) {
            & taskkill.exe /PID $script:LiveProcess.Id /T /F 2>$null | Out-Null
        }
    }
    catch { }
    try { $script:LiveProcess.WaitForExitAndDrain() } catch { }
    Drain-LiveOutput
    try { $script:LiveProcess.Dispose() } catch { }
    $script:LiveProcess = $null
    $script:ActiveProcessKind = ''
    $script:PendingBackup = $false
    $script:BackupQueue = $null
    Set-ActionLock $false
}

function Complete-ActiveProcess {
    if ($null -eq $script:LiveProcess) { return }

    $script:LiveProcess.WaitForExitAndDrain()
    Drain-LiveOutput
    $exitCode = $script:LiveProcess.ExitCode
    $stdout = $script:LiveProcess.StandardOutput
    $stderr = $script:LiveProcess.StandardError
    $kind = $script:ActiveProcessKind
    $script:LiveProcess.Dispose()
    $script:LiveProcess = $null
    $script:ActiveProcessKind = ''

    if ($kind -eq 'RemoteRefresh') {
        Complete-RemoteRefresh $exitCode $stdout $stderr
        return
    }

    if ($kind -like 'Backup:*') {
        if ($exitCode -ne 0) {
            $script:BackupQueue = $null
            $script:PendingBackup = $false
            $script:LastBackupSucceeded = $false
            Set-BackupResultImage 'Error'
            Set-BackupState 'ERROR'
            Append-TerminalLine ("ERROR: Git command failed with exit code $exitCode. Local commits are preserved.")
            Set-ActionLock $false
            Refresh-LocalUi $true
            return
        }
        Start-NextBackupCommand
    }
}

function Open-GitHubRepository {
    if ($null -eq $script:CurrentState) { Refresh-LocalUi $false }
    $url = Convert-GitRemoteToWebUrl $script:CurrentState.OriginUrl
    if ([string]::IsNullOrWhiteSpace($url)) {
        Append-TerminalLine 'OPEN GITHUB: origin URL is missing or unsupported.'
        return
    }
    try { Start-Process $url | Out-Null }
    catch { Append-TerminalLine ('OPEN GITHUB ERROR: ' + $_.Exception.Message) }
}

function Show-HistoryWindow {
    $result = Invoke-GitCapture $script:ResolvedProjectRoot @('log', '-30', '--date=format-local:%Y-%m-%d %H:%M', '--pretty=format:%h%x09%ad%x09%an%x09%s')
    if ($result.ExitCode -ne 0) {
        Append-TerminalLine ('HISTORY ERROR: ' + $result.StdErr.Trim())
        return
    }

    $history = New-Object System.Windows.Forms.Form
    $history.Text = 'LERT WHEELBARROWS - GIT HISTORY'
    $history.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterParent
    $history.ClientSize = New-Object System.Drawing.Size -ArgumentList 900, 520
    $history.MinimumSize = New-Object System.Drawing.Size -ArgumentList 760, 420
    $history.BackColor = $script:Colors.Back
    if ($null -ne $script:WindowIcon) { $history.Icon = $script:WindowIcon }

    $title = New-Object System.Windows.Forms.Label
    $title.Text = 'LERT WHEELBARROWS - GIT HISTORY (LAST 30 COMMITS)'
    $title.Location = New-Object System.Drawing.Point -ArgumentList 18, 14
    $title.Size = New-Object System.Drawing.Size -ArgumentList 850, 30
    $title.Font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', 12, ([System.Drawing.FontStyle]::Bold)
    $title.ForeColor = $script:Colors.Accent
    $history.Controls.Add($title)

    $list = New-Object System.Windows.Forms.ListView
    $list.Location = New-Object System.Drawing.Point -ArgumentList 18, 55
    $list.Size = New-Object System.Drawing.Size -ArgumentList 864, 445
    $list.Anchor = [System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
    $list.View = [System.Windows.Forms.View]::Details
    $list.FullRowSelect = $true
    $list.GridLines = $true
    $list.BackColor = $script:Colors.Panel
    $list.ForeColor = $script:Colors.Text
    [void]$list.Columns.Add('HASH', 85)
    [void]$list.Columns.Add('DATE', 145)
    [void]$list.Columns.Add('AUTHOR', 150)
    [void]$list.Columns.Add('MESSAGE', 450)

    foreach ($line in $result.StdOut.Replace("`r`n", "`n").Split([char]10)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split "`t", 4
        if ($parts.Count -lt 4) { continue }
        $item = New-Object System.Windows.Forms.ListViewItem -ArgumentList $parts[0]
        [void]$item.SubItems.Add($parts[1])
        [void]$item.SubItems.Add($parts[2])
        [void]$item.SubItems.Add($parts[3])
        [void]$list.Items.Add($item)
    }
    $history.Controls.Add($list)
    [void]$history.ShowDialog($script:Form)
}

function New-StatusPanel {
    param([string]$Title, [int]$X, [int]$Y, [int]$Width)

    $panel = New-Object System.Windows.Forms.Panel
    $panel.Location = New-Object System.Drawing.Point -ArgumentList $X, $Y
    $panel.Size = New-Object System.Drawing.Size -ArgumentList $Width, 64
    $panel.BackColor = $script:Colors.Panel
    $panel.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle

    $header = New-Object System.Windows.Forms.Label
    $header.Text = $Title
    $header.Location = New-Object System.Drawing.Point -ArgumentList 8, 5
    $header.Size = New-Object System.Drawing.Size -ArgumentList ($Width - 16), 20
    $header.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
    $header.Font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', 8, ([System.Drawing.FontStyle]::Bold)
    $header.ForeColor = $script:Colors.Muted
    $panel.Controls.Add($header)

    $value = New-Object System.Windows.Forms.Label
    $value.Text = 'WAITING'
    $value.Location = New-Object System.Drawing.Point -ArgumentList 8, 27
    $value.Size = New-Object System.Drawing.Size -ArgumentList ($Width - 16), 28
    $value.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
    $value.Font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', 11, ([System.Drawing.FontStyle]::Bold)
    $value.ForeColor = $script:Colors.Warn
    $panel.Controls.Add($value)

    $script:Form.Controls.Add($panel)
    return $value
}

function New-ActionButton {
    param([string]$Text, [int]$X, [int]$Y, [int]$Width, $Image)

    $button = New-Object System.Windows.Forms.Button
    $button.Text = $Text
    $button.Location = New-Object System.Drawing.Point -ArgumentList $X, $Y
    $button.Size = New-Object System.Drawing.Size -ArgumentList $Width, 48
    $button.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
    $button.FlatAppearance.BorderColor = $script:Colors.Accent
    $button.FlatAppearance.BorderSize = 1
    $button.BackColor = $script:Colors.Panel
    $button.ForeColor = $script:Colors.Text
    $button.Font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', 9, ([System.Drawing.FontStyle]::Bold)
    if ($null -ne $Image) {
        $button.Image = $Image
        $button.ImageAlign = [System.Drawing.ContentAlignment]::MiddleLeft
        $button.TextImageRelation = [System.Windows.Forms.TextImageRelation]::ImageBeforeText
    }
    $script:Form.Controls.Add($button)
    return $button
}

function Start-GitHubBackupTool {
    param([string]$RequestedProjectRoot)

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()
    Initialize-LiveProcessPump

    $script:ResolvedProjectRoot = Resolve-ProjectRoot $RequestedProjectRoot
    $gitVersion = Invoke-GitCapture $script:ResolvedProjectRoot @('--version')
    if ($gitVersion.ExitCode -ne 0) { throw 'Git executable is unavailable.' }

    $script:Colors = [pscustomobject]@{
        Back         = [System.Drawing.Color]::FromArgb(10, 16, 20)
        Panel        = [System.Drawing.Color]::FromArgb(17, 29, 34)
        TerminalBack = [System.Drawing.Color]::FromArgb(5, 10, 12)
        Text         = [System.Drawing.Color]::FromArgb(223, 235, 236)
        Muted        = [System.Drawing.Color]::FromArgb(132, 154, 157)
        Accent       = [System.Drawing.Color]::FromArgb(52, 210, 205)
        Good         = [System.Drawing.Color]::FromArgb(84, 224, 143)
        Warn         = [System.Drawing.Color]::FromArgb(245, 192, 89)
        Bad          = [System.Drawing.Color]::FromArgb(242, 99, 99)
    }

    $script:UiImages = @{}
    foreach ($key in $script:AssetFiles.Keys) {
        if ($key -eq 'WindowIcon') { continue }
        $script:UiImages[$key] = Load-UiImage (Get-AssetPath $key)
    }
    $script:WindowIcon = Load-UiIcon (Get-AssetPath 'WindowIcon')

    $form = New-Object System.Windows.Forms.Form
    $script:Form = $form
    $form.Text = 'LERT WHEELBARROWS - GITHUB BACKUP TOOL'
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedSingle
    $form.MaximizeBox = $false
    $form.ClientSize = New-Object System.Drawing.Size -ArgumentList 1240, 690
    $form.BackColor = $script:Colors.Back
    if ($null -ne $script:WindowIcon) { $form.Icon = $script:WindowIcon }
    if ($null -ne $script:UiImages['Background']) {
        $form.BackgroundImage = $script:UiImages['Background']
        $form.BackgroundImageLayout = [System.Windows.Forms.ImageLayout]::Stretch
    }

    $titlePicture = New-Object System.Windows.Forms.PictureBox
    $titlePicture.Location = New-Object System.Drawing.Point -ArgumentList 18, 6
    $titlePicture.Size = New-Object System.Drawing.Size -ArgumentList 250, 96
    $titlePicture.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
    $titlePicture.BackColor = [System.Drawing.Color]::Transparent
    if ($null -ne $script:UiImages['GameTitle']) { $titlePicture.Image = $script:UiImages['GameTitle'] }
    $form.Controls.Add($titlePicture)

    $logoPicture = New-Object System.Windows.Forms.PictureBox
    $logoPicture.Location = New-Object System.Drawing.Point -ArgumentList 287, 20
    $logoPicture.Size = New-Object System.Drawing.Size -ArgumentList 48, 48
    $logoPicture.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
    $logoPicture.BackColor = [System.Drawing.Color]::Transparent
    if ($null -ne $script:UiImages['ToolLogo']) { $logoPicture.Image = $script:UiImages['ToolLogo'] }
    $form.Controls.Add($logoPicture)

    $toolTitle = New-Object System.Windows.Forms.Label
    $toolTitle.Text = 'WHEELBARROWS — GITHUB'
    $toolTitle.Location = New-Object System.Drawing.Point -ArgumentList 345, 19
    $toolTitle.Size = New-Object System.Drawing.Size -ArgumentList 560, 33
    $toolTitle.Font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', 18, ([System.Drawing.FontStyle]::Bold)
    $toolTitle.ForeColor = $script:Colors.Accent
    $toolTitle.BackColor = [System.Drawing.Color]::Transparent
    $form.Controls.Add($toolTitle)

    $subtitle = New-Object System.Windows.Forms.Label
    $subtitle.Text = 'Вихідний код мода  |  Стан Git  |  Журнал команд  |  Історія змін'
    $subtitle.Location = New-Object System.Drawing.Point -ArgumentList 348, 53
    $subtitle.Size = New-Object System.Drawing.Size -ArgumentList 700, 24
    $subtitle.Font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', 9
    $subtitle.ForeColor = $script:Colors.Muted
    $subtitle.BackColor = [System.Drawing.Color]::Transparent
    $form.Controls.Add($subtitle)

    $statusY = 105
    $script:ProjectValue = New-StatusPanel 'PROJECT' 20 $statusY 285
    $script:WorktreeValue = New-StatusPanel 'WORKTREE' 325 $statusY 285
    $script:RemoteValue = New-StatusPanel 'REMOTE' 630 $statusY 285
    $script:SyncValue = New-StatusPanel 'SYNC' 935 $statusY 285

    $script:WorktreeStatusIcon = New-Object System.Windows.Forms.PictureBox
    $script:WorktreeStatusIcon.Location = New-Object System.Drawing.Point -ArgumentList 340, ($statusY + 31)
    $script:WorktreeStatusIcon.Size = New-Object System.Drawing.Size -ArgumentList 24, 24
    $script:WorktreeStatusIcon.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
    $script:WorktreeStatusIcon.BackColor = [System.Drawing.Color]::Transparent
    $form.Controls.Add($script:WorktreeStatusIcon)
    $script:WorktreeStatusIcon.BringToFront()

    $script:RemoteStatusIcon = New-Object System.Windows.Forms.PictureBox
    $script:RemoteStatusIcon.Location = New-Object System.Drawing.Point -ArgumentList 645, ($statusY + 31)
    $script:RemoteStatusIcon.Size = New-Object System.Drawing.Size -ArgumentList 24, 24
    $script:RemoteStatusIcon.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
    $script:RemoteStatusIcon.BackColor = [System.Drawing.Color]::Transparent
    $form.Controls.Add($script:RemoteStatusIcon)
    $script:RemoteStatusIcon.BringToFront()

    $actionY = 184
    $script:BackupButton = New-ActionButton 'ЗАВАНТАЖИТИ' 20 $actionY 210 $script:UiImages['Backup']
    $script:RefreshButton = New-ActionButton 'ОНОВИТИ СТАН' 250 $actionY 210 $script:UiImages['Refresh']
    $script:GitHubButton = New-ActionButton 'ВІДКРИТИ GITHUB' 480 $actionY 210 $script:UiImages['GitHub']
    $script:HistoryButton = New-ActionButton 'ІСТОРІЯ' 710 $actionY 210 $script:UiImages['History']

    $script:BackupResultPicture = New-Object System.Windows.Forms.PictureBox
    $script:BackupResultPicture.Location = New-Object System.Drawing.Point -ArgumentList 955, 184
    $script:BackupResultPicture.Size = New-Object System.Drawing.Size -ArgumentList 48, 48
    $script:BackupResultPicture.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
    $script:BackupResultPicture.BackColor = [System.Drawing.Color]::Transparent
    $form.Controls.Add($script:BackupResultPicture)

    $script:BackupStateLabel = New-Object System.Windows.Forms.Label
    $script:BackupStateLabel.Location = New-Object System.Drawing.Point -ArgumentList 1012, 184
    $script:BackupStateLabel.Size = New-Object System.Drawing.Size -ArgumentList 208, 48
    $script:BackupStateLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft
    $script:BackupStateLabel.Font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', 10, ([System.Drawing.FontStyle]::Bold)
    $script:BackupStateLabel.BackColor = [System.Drawing.Color]::Transparent
    $form.Controls.Add($script:BackupStateLabel)
    Set-BackupState 'READY'

    $context = New-Object System.Windows.Forms.Label
    $script:ContextLabel = $context
    $context.Location = New-Object System.Drawing.Point -ArgumentList 20, 239
    $context.Size = New-Object System.Drawing.Size -ArgumentList 1200, 22
    $context.ForeColor = $script:Colors.Muted
    $context.Font = New-Object System.Drawing.Font -ArgumentList 'Consolas', 8
    $context.BackColor = [System.Drawing.Color]::Transparent
    $form.Controls.Add($context)

    $changedHeader = New-Object System.Windows.Forms.Label
    $changedHeader.Text = 'ЗМІНЕНІ ФАЙЛИ'
    $changedHeader.Location = New-Object System.Drawing.Point -ArgumentList 20, 268
    $changedHeader.Size = New-Object System.Drawing.Size -ArgumentList 1160, 24
    $changedHeader.Font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', 9, ([System.Drawing.FontStyle]::Bold)
    $changedHeader.ForeColor = $script:Colors.Text
    $changedHeader.BackColor = [System.Drawing.Color]::Transparent
    $form.Controls.Add($changedHeader)

    $changedIcon = New-Object System.Windows.Forms.PictureBox
    $changedIcon.Location = New-Object System.Drawing.Point -ArgumentList 1176, 267
    $changedIcon.Size = New-Object System.Drawing.Size -ArgumentList 24, 24
    $changedIcon.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
    $changedIcon.BackColor = [System.Drawing.Color]::Transparent
    if ($null -ne $script:UiImages['ChangedFiles']) { $changedIcon.Image = $script:UiImages['ChangedFiles'] }
    $form.Controls.Add($changedIcon)

    $changed = New-Object System.Windows.Forms.ListView
    $script:ChangedList = $changed
    $changed.Location = New-Object System.Drawing.Point -ArgumentList 20, 296
    $changed.Size = New-Object System.Drawing.Size -ArgumentList 1200, 136
    $changed.View = [System.Windows.Forms.View]::Details
    $changed.FullRowSelect = $true
    $changed.GridLines = $true
    $changed.HideSelection = $false
    $changed.BackColor = $script:Colors.Panel
    $changed.ForeColor = $script:Colors.Text
    $changed.Font = New-Object System.Drawing.Font -ArgumentList 'Consolas', 9
    [void]$changed.Columns.Add('STATUS', 72)
    [void]$changed.Columns.Add('TYPE', 110)
    [void]$changed.Columns.Add('PATH', 990)
    $form.Controls.Add($changed)

    $terminalHeader = New-Object System.Windows.Forms.Label
    $terminalHeader.Text = '●  LIVE TERMINAL'
    $terminalHeader.Location = New-Object System.Drawing.Point -ArgumentList 20, 442
    $terminalHeader.Size = New-Object System.Drawing.Size -ArgumentList 250, 24
    $terminalHeader.Font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', 9, ([System.Drawing.FontStyle]::Bold)
    $terminalHeader.ForeColor = $script:Colors.Bad
    $terminalHeader.BackColor = [System.Drawing.Color]::Transparent
    $form.Controls.Add($terminalHeader)
    $script:LiveTerminalLabel = $terminalHeader

    $clear = New-Object System.Windows.Forms.Button
    $clear.Location = New-Object System.Drawing.Point -ArgumentList 1174, 438
    $clear.Size = New-Object System.Drawing.Size -ArgumentList 46, 32
    $clear.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
    $clear.FlatAppearance.BorderColor = $script:Colors.Accent
    $clear.BackColor = $script:Colors.Panel
    $clear.ForeColor = $script:Colors.Text
    $clear.Text = 'CLR'
    if ($null -ne $script:UiImages['ClearLog']) { $clear.Image = $script:UiImages['ClearLog']; $clear.Text = '' }
    $form.Controls.Add($clear)

    $log = New-Object System.Windows.Forms.RichTextBox
    $script:LogBox = $log
    $log.Location = New-Object System.Drawing.Point -ArgumentList 20, 473
    $log.Size = New-Object System.Drawing.Size -ArgumentList 1200, 190
    $log.ReadOnly = $true
    $log.BackColor = $script:Colors.TerminalBack
    $log.ForeColor = $script:Colors.Text
    $log.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
    $log.Font = New-Object System.Drawing.Font -ArgumentList 'Consolas', 9
    $log.DetectUrls = $false
    $form.Controls.Add($log)

    $script:BackupButton.Add_Click({ Start-Backup })
    $script:RefreshButton.Add_Click({
        if (-not $script:UiBusy) {
            Append-TerminalLine ''
            Append-TerminalLine '=== REFRESH ==='
            Refresh-LocalUi $true
        }
    })
    $script:GitHubButton.Add_Click({ Open-GitHubRepository })
    $script:HistoryButton.Add_Click({ Show-HistoryWindow })
    $clear.Add_Click({ $script:LogBox.Clear() })

    $timer = New-Object System.Windows.Forms.Timer
    $timer.Interval = 100
    $timer.Add_Tick({
        Drain-LiveOutput
        $script:LiveBlinkTicks++
        if ($script:LiveBlinkTicks -ge 5) {
            if ($script:LiveTerminalLabel.Text.StartsWith('●')) { $script:LiveTerminalLabel.Text = '○  LIVE TERMINAL' }
            else { $script:LiveTerminalLabel.Text = '●  LIVE TERMINAL' }
            $script:LiveBlinkTicks = 0
        }

        if ($null -ne $script:LiveProcess) {
            try {
                if ($script:LiveProcess.HasExited) { Complete-ActiveProcess }
            }
            catch {
                Append-TerminalLine ('PROCESS MONITOR ERROR: ' + $_.Exception.Message)
                try { $script:LiveProcess.Dispose() } catch { }
                $script:LiveProcess = $null
                $script:ActiveProcessKind = ''
                $script:PendingBackup = $false
                Set-BackupResultImage 'Error'
                Set-BackupState 'ERROR'
                Set-ActionLock $false
            }
        }
    })

    $form.Add_FormClosing({
        param($sender, $eventArgs)
        if ($null -ne $script:LiveProcess -and -not $script:LiveProcess.HasExited) {
            $answer = [System.Windows.Forms.MessageBox]::Show(
                'Git operation is still running. Close the tool anyway?',
                'LERT WHEELBARROWS - GitHub Backup Tool',
                [System.Windows.Forms.MessageBoxButtons]::YesNo,
                [System.Windows.Forms.MessageBoxIcon]::Warning
            )
            if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
                $eventArgs.Cancel = $true
                return
            }
            Stop-LiveProcess
        }
    })

    Append-TerminalLine ('LERT WHEELBARROWS GitHub Backup Tool')
    Append-TerminalLine ('Project: ' + $script:ResolvedProjectRoot)
    Append-TerminalLine ($gitVersion.StdOut.Trim())
    Refresh-LocalUi $true
    $timer.Start()
    $script:UiStarted = $true
    [void]$form.ShowDialog()
    $timer.Stop()

    if ($null -ne $script:LiveProcess) {
        try { $script:LiveProcess.Dispose() } catch { }
        $script:LiveProcess = $null
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if (Test-Path -LiteralPath $script:StartupErrorLog) { Remove-Item -LiteralPath $script:StartupErrorLog -Force -ErrorAction SilentlyContinue }
        Start-GitHubBackupTool $ProjectRoot
        exit 0
    }
    catch {
        $message = $_ | Out-String
        if (-not $script:UiStarted) {
            try { [System.IO.File]::WriteAllText($script:StartupErrorLog, $message, (New-Object System.Text.UTF8Encoding -ArgumentList $true)) } catch { }
        }
        [Console]::Error.WriteLine($message)
        exit 1
    }
}
