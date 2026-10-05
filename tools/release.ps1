<#
.SYNOPSIS
  Fecha uma versão: Não lançado -> versão nos dois CHANGELOGs, Directory.Build.props, commit e tag. Não dá push.
.EXAMPLE
  tools\release.ps1 -Version 1.7.0-beta.1
#>
param([Parameter(Mandatory)][string]$Version, [switch]$NoGit)
$ErrorActionPreference = 'Stop'
$Version = $Version.TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Versão inválida '$Version' (use MAJOR.MINOR.PATCH ou MAJOR.MINOR.PATCH-beta.N)." }
$root = Split-Path $PSScriptRoot
Set-Location $root
if (-not $NoGit) {
    if (git status --porcelain --untracked-files=no) { throw 'Há mudanças não commitadas. Commite antes de lançar.' }
    if (git tag -l "v$Version") { throw "A tag v$Version já existe." }
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
$date = Get-Date -Format 'yyyy-MM-dd'
$repo = 'https://github.com/bps2414/ferry'

$out = @{}
foreach ($c in @(@{ File = 'CHANGELOG.md'; Head = 'Unreleased' }, @{ File = 'CHANGELOG.pt-BR.md'; Head = 'Não lançado' })) {
    $path = Join-Path $root $c.File
    $text = [IO.File]::ReadAllText($path, $utf8)
    $h = [regex]::Escape($c.Head)
    $body = [regex]::Match($text, "(?s)## \[$h\]\r?\n(.*?)\r?\n## \[").Groups[1].Value
    if ($body -notmatch '(?m)^- ') { throw "$($c.File): a seção [$($c.Head)] está vazia." }
    if ($text -match "(?m)^## \[$([regex]::Escape($Version))\]") { throw "$($c.File) já tem a versão $Version." }
    $prev = [regex]::Match($text, "(?m)^\[$h\]: .*/compare/v(.+?)\.\.\.HEAD").Groups[1].Value
    if (-not $prev) { throw "$($c.File): link [$($c.Head)] não encontrado no rodapé." }
    $text = $text -replace "(?m)^## \[$h\]", "## [$($c.Head)]`n`n## [$Version] - $date"
    $text = $text -replace "(?m)^\[$h\]: .*$", "[$($c.Head)]: $repo/compare/v$Version...HEAD`n[$Version]: $repo/compare/v$prev...v$Version"
    $out[$path] = $text
}

# só grava depois de validar os dois changelogs
foreach ($k in $out.Keys) { [IO.File]::WriteAllText($k, $out[$k], $utf8) }

$props = Join-Path $root 'Directory.Build.props'
$p = [IO.File]::ReadAllText($props, $utf8) -replace '<Version>.*?</Version>', "<Version>$Version</Version>"
[IO.File]::WriteAllText($props, $p, $utf8)

if ($NoGit) { Write-Host "Versão $Version aplicada (sem git)."; return }
git add CHANGELOG.md CHANGELOG.pt-BR.md Directory.Build.props
git commit -m "chore(release): v$Version"
git tag -a "v$Version" -m "Ferry v$Version"
Write-Host "Pronto. Confira e publique com: git push --follow-tags"
