# print-rule-list.ps1
# Lists rule .md files loaded into agent context, both user-global
# (~/.agents/rules/) and project-local (.agents/rules/). Output goes to
# the file passed via -Output (one scope per line); MSBuild reads the
# file and emits each line as a build-time reminder.
#
# Invoked by VerifyRuleAwareness in Comuki.Build.Tools.targets.
# Pure read-only — does not modify any files.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Output,

    [Parameter(Mandatory = $false)]
    [string]$RepoRoot
)

$ErrorActionPreference = 'SilentlyContinue'

# User-global rules (cross-language corpus). USERPROFILE on Windows,
# HOME on POSIX. The MSBuild target already resolved $(HOME) / $(USERPROFILE),
# but we accept either so the script can be re-run manually.
$userHome = $env:USERPROFILE
if ([string]::IsNullOrEmpty($userHome)) { $userHome = $env:HOME }

$userScopes = @(
    @{ Name = 'csharp';       Path = (Join-Path $userHome '.agents/rules/csharp') },
    @{ Name = 'typescript';   Path = (Join-Path $userHome '.agents/rules/typescript') },
    @{ Name = 'process';      Path = (Join-Path $userHome '.agents/rules/process') },
    @{ Name = 'observability'; Path = (Join-Path $userHome '.agents/rules/observability') }
)

# Project-local rules (this repo). MSBuild passes $(RepoRoot) explicitly;
# fall back to a 4-up walk from this script's directory when invoked by hand.
if ([string]::IsNullOrEmpty($RepoRoot)) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = (Resolve-Path (Join-Path $scriptRoot '..\..\..\..')).Path
}

$projectScopes = @(
    @{ Name = 'project(coding)'; Path = (Join-Path $RepoRoot '.agents/rules/coding') },
    @{ Name = 'project(process)'; Path = (Join-Path $RepoRoot '.agents/rules/process') }
)

$lines = @()

function Write-ScopeList {
    param(
        [string]$ScopeName,
        [string]$ScopePath
    )

    if (-not (Test-Path -LiteralPath $ScopePath)) {
        $script:lines += "[$ScopeName]: not found at $ScopePath"
        return
    }

    $files = @(Get-ChildItem -LiteralPath $ScopePath -Recurse -Filter '*.md' -File |
               Sort-Object -Property FullName |
               ForEach-Object { $_.BaseName })

    if ($files.Count -eq 0) {
        $script:lines += "[$ScopeName]: 0 files at $ScopePath"
        return
    }

    $preview = $files -join ', '
    $script:lines += "[$ScopeName]: $($files.Count) files -- $preview"
}

foreach ($scope in $userScopes)     { Write-ScopeList -ScopeName $scope.Name -ScopePath $scope.Path }
foreach ($scope in $projectScopes)  { Write-ScopeList -ScopeName $scope.Name -ScopePath $scope.Path }

$parent = Split-Path -Parent $Output
if ($parent -and -not (Test-Path -LiteralPath $parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}

Set-Content -LiteralPath $Output -Value $lines -Encoding utf8

Write-Host "[print-rule-list] wrote $($lines.Count) scope lines to $Output"
