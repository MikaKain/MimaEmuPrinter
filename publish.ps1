<#
.SYNOPSIS
    Compile MimaEmuPrinter en mode standalone (self-contained) dans le dossier publish/.

.DESCRIPTION
    Le resultat n'a pas besoin du runtime .NET sur la machine cible : le runtime est embarque.
    Un sous-dossier par plateforme est cree : publish/<runtime>/ (ex. publish/win-x64/).

.PARAMETER Runtime
    Plateforme(s) cible(s), ex. win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64.
    Par defaut : win-x64.

.PARAMETER Configuration
    Configuration de compilation. Par defaut : Release.

.PARAMETER SingleFile
    Produit un seul fichier executable (le runtime et les bibliotheques sont embarques dedans).

.PARAMETER NoClean
    Ne vide pas le dossier publish/<runtime>/ avant la publication.

.EXAMPLE
    ./publish.ps1
    ./publish.ps1 -Runtime win-x64, linux-x64, osx-arm64
    ./publish.ps1 -SingleFile
#>
[CmdletBinding()]
param(
	[string[]]$Runtime = @('win-x64'),
	[string]$Configuration = 'Release',
	[switch]$SingleFile,
	[switch]$NoClean
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src/MimaEmuPrinter.App/MimaEmuPrinter.App.csproj'
$publishRoot = Join-Path $root 'publish'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
	throw "Le SDK .NET 10 est introuvable (commande 'dotnet' absente du PATH)."
}

foreach ($rid in $Runtime) {
	$output = Join-Path $publishRoot $rid

	if (-not $NoClean -and (Test-Path $output)) {
		Write-Host "Nettoyage de $output" -ForegroundColor DarkGray
		Remove-Item -Recurse -Force $output
	}

	Write-Host "Publication standalone ($rid, $Configuration)..." -ForegroundColor Cyan

	$arguments = @(
		'publish', $project,
		'-c', $Configuration,
		'-r', $rid,
		'--self-contained', 'true',
		'-o', $output,
		'-p:DebugType=None',
		'-p:DebugSymbols=false'
	)

	if ($SingleFile) {
		$arguments += @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true')
	}

	& dotnet @arguments
	if ($LASTEXITCODE -ne 0) {
		throw "La publication a echoue pour $rid (code $LASTEXITCODE)."
	}

	$size = (Get-ChildItem $output -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
	Write-Host ("OK : {0} ({1:N1} Mo)" -f $output, $size) -ForegroundColor Green
}
