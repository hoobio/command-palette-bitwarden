param([Parameter(Mandatory)][ValidateSet('Mark', 'Check')][string]$Mode)

$ErrorActionPreference = 'Stop'
$hook = [Console]::In.ReadToEnd() | ConvertFrom-Json
$agent = if ($hook.agent_id) { $hook.agent_id } else { 'main' }
$marker = Join-Path ([IO.Path]::GetTempPath()) "claude-dev-deploy-skill\$($hook.session_id)_$agent"

if ($Mode -eq 'Mark') {
    $loaded = ($hook.tool_name -eq 'Skill' -and $hook.tool_input.skill -eq 'dev-deploy') -or
        ($hook.tool_name -eq 'Read' -and $hook.tool_input.file_path -match 'skills[\\/]dev-deploy[\\/]SKILL\.md$')
    if ($loaded) {
        New-Item -ItemType Directory -Force (Split-Path $marker) | Out-Null
        Set-Content -Path $marker -Value (Get-Date -Format o)
    }
    exit 0
}

$cmd = [string]$hook.tool_input.command
$stopsExtension = $cmd -match '(?i)(Stop-Process|taskkill|\bkill\b)' -and $cmd -match '(?i)HoobiBitwardenCommandPaletteExtension'
$touchesBuild = $cmd -match '(?i)\bdotnet\s+(build|test|publish)\b|(Add|Remove)-AppxPackage'

$reason = if ($stopsExtension -and $cmd -notmatch '(?i)Microsoft\.PowerToysWin32|PowerToys\.exe') {
    'This command stops the extension process without restarting PowerToys. Command Palette keeps the dead connection (0x800706BA) until PowerToys restarts. Stop PowerToys and Microsoft.CmdPal.UI too and start PowerToys in the same command, per the dev-deploy skill.'
}
elseif (($stopsExtension -or $touchesBuild) -and -not (Test-Path $marker)) {
    'Load the dev-deploy skill (.claude/skills/dev-deploy/SKILL.md, Skill tool or Read) before building, testing, deploying or stopping the extension in this repo, then retry.'
}
if (-not $reason) { exit 0 }

@{
    hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision       = 'deny'
        permissionDecisionReason = $reason
    }
} | ConvertTo-Json -Depth 5 -Compress
exit 0
