param([Parameter(Mandatory=$true)][string]$ProjectRoot)
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$expectedRepo='https://github.com/LERT23/LERT_Wheelbarrows-RIMWORLD_MOD.git'
$root=[IO.Path]::GetFullPath($ProjectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$toolRoot=$PSScriptRoot
$utf8=New-Object Text.UTF8Encoding($true)
$sessionLock=$null

function Quote-Argument([string]$Value) {
    if($Value.Length -gt 0 -and $Value -notmatch '[\s"]'){return $Value}
    # Windows CommandLineToArgvW-compatible quoting, including trailing slashes.
    return '"'+([regex]::Replace([regex]::Replace($Value,'(\\*)"','$1$1\"'),'(\\+)$','$1$1'))+'"'
}

function Invoke-ProjectGit([string]$Directory,[string[]]$GitArgs,[switch]$AllowFailure) {
    $psi=New-Object Diagnostics.ProcessStartInfo
    $psi.FileName=$script:gitExe
    $argsToUse=@('-C',$Directory)+@($GitArgs)
    $psi.Arguments=(@($argsToUse|ForEach-Object{Quote-Argument $_}) -join ' ')
    $psi.UseShellExecute=$false;$psi.CreateNoWindow=$true
    $psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
    $psi.StandardOutputEncoding=New-Object Text.UTF8Encoding($false)
    $psi.StandardErrorEncoding=New-Object Text.UTF8Encoding($false)
    $process=New-Object Diagnostics.Process
    $process.StartInfo=$psi
    try {
        [void]$process.Start()
        $stdout=$process.StandardOutput.ReadToEndAsync()
        $stderr=$process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $result=[pscustomobject]@{ExitCode=$process.ExitCode;StdOut=$stdout.GetAwaiter().GetResult();StdErr=$stderr.GetAwaiter().GetResult()}
        if($result.ExitCode -ne 0 -and !$AllowFailure){throw ("git {0} failed ({1}).`n{2}" -f ($GitArgs -join ' '),$result.ExitCode,$result.StdErr)}
        return $result
    }finally{$process.Dispose()}
}

function Copy-MissingRemoteFiles([string]$From,[string]$To) {
    foreach($entry in Get-ChildItem -LiteralPath $From -Force){
        if($entry.Name -eq '.git'){continue}
        if(($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Remote checkout contains a reparse point; copying stopped.'}
        $destination=Join-Path $To $entry.Name
        if($entry.PSIsContainer){
            if(!(Test-Path -LiteralPath $destination)){[void](New-Item -ItemType Directory -Path $destination)}
            if(!(Test-Path -LiteralPath $destination -PathType Container)){throw "File/directory collision: $destination"}
            Copy-MissingRemoteFiles $entry.FullName $destination
        }elseif(!(Test-Path -LiteralPath $destination)){
            Copy-Item -LiteralPath $entry.FullName -Destination $destination
        }
    }
}

try {
    $script:gitExe=(Get-Command git.exe -ErrorAction Stop).Source
    if(!(Test-Path -LiteralPath (Join-Path $root 'About\About.xml')) -or !(Test-Path -LiteralPath (Join-Path $root 'Source\LertWheelbarrows.csproj'))){
        throw 'Mod source files are missing. Extract the entire package and start its root BAT file.'
    }
    $runtime=Join-Path $toolRoot '.runtime'
    [void](New-Item -ItemType Directory -Path $runtime -Force)
    try { $sessionLock=[IO.File]::Open((Join-Path $runtime 'session.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None) }
    catch { throw 'Another publisher window is using this folder. Close it before starting a second copy.' }
    $gitDirectory=Join-Path $root '.git'
    if(!(Test-Path -LiteralPath $gitDirectory)){
        Write-Host 'Preparing the existing GitHub history. No upload is performed yet.'
        $runtime=Join-Path $toolRoot '.runtime'
        [void](New-Item -ItemType Directory -Path $runtime -Force)
        $stage=Join-Path $runtime ('bootstrap-'+[Guid]::NewGuid().ToString('N'))
        [void](New-Item -ItemType Directory -Path $stage)
        $clone=Invoke-ProjectGit $root @('clone','--origin','origin','--',$expectedRepo,$stage)
        if($clone.StdErr){Write-Host $clone.StdErr.Trim()}
        [void](Invoke-ProjectGit $stage @('rev-parse','--verify','HEAD'))
        # LICENSE and other remote-only files survive; local mod files remain intact.
        Copy-MissingRemoteFiles $stage $root
        $sourceGit=[IO.Path]::GetFullPath((Join-Path $stage '.git'))
        $targetGit=[IO.Path]::GetFullPath($gitDirectory)
        $expectedStage=[IO.Path]::GetFullPath($stage)+[IO.Path]::DirectorySeparatorChar
        if(!$sourceGit.StartsWith($expectedStage,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetDirectoryName($targetGit) -ne $root){
            throw 'Unexpected Git metadata relocation path.'
        }
        # A normal clone has no fixed core.worktree; its metadata remains valid here.
        if(Test-Path -LiteralPath $targetGit){throw 'Git metadata already exists; relocation stopped.'}
        Move-Item -LiteralPath $sourceGit -Destination $targetGit -Force
    }
    $top=(Invoke-ProjectGit $root @('rev-parse','--show-toplevel')).StdOut.Trim()
    if([IO.Path]::GetFullPath($top).TrimEnd([IO.Path]::DirectorySeparatorChar) -ne $root){throw 'The BAT must run in its own repository, not inside another checkout.'}
    $origin=(Invoke-ProjectGit $root @('remote','get-url','origin')).StdOut.Trim()
    if($origin -notin @($expectedRepo,$expectedRepo.Substring(0,$expectedRepo.Length-4),'git@github.com:LERT23/LERT_Wheelbarrows-RIMWORLD_MOD.git')){
        throw 'origin points to a different repository. The tool did not change it.'
    }
    # Preserve configured identities. Set a GitHub noreply identity only when absent.
    $name=Invoke-ProjectGit $root @('config','user.name') -AllowFailure
    if(!$name.StdOut.Trim()){[void](Invoke-ProjectGit $root @('config','--local','user.name','LERT23'))}
    $email=Invoke-ProjectGit $root @('config','user.email') -AllowFailure
    if(!$email.StdOut.Trim()){[void](Invoke-ProjectGit $root @('config','--local','user.email','134075607+LERT23@users.noreply.github.com'))}
    Write-Host 'Ready. Use the UPLOAD button in the GUI to commit and push.'
    & (Join-Path $toolRoot 'run_github_backup_gui.ps1') -ProjectRoot $root
    exit $LASTEXITCODE
}catch{
    $message=$_|Out-String
    [IO.File]::WriteAllText((Join-Path $toolRoot 'startup_error.log'),$message,$utf8)
    [Console]::Error.WriteLine($message)
    exit 1
}
finally { if($null -ne $sessionLock){$sessionLock.Dispose()} }
