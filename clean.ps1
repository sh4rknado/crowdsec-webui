<#
.SYNOPSIS
    Supprime récursivement tous les dossiers bin et obj d'un répertoire.
.DESCRIPTION
    Ce script parcourt le chemin spécifié (ou le dossier courant) et supprime 
    les dossiers nommés "bin" et "obj" associés aux projets Visual Studio.
#>

param(
    [string]$TargetDirectory = $PSScriptRoot
)

# Sécurité : Vérifie si le dossier existe
if (-not (Test-Path $TargetDirectory)) {
    Write-Host "Le chemin spécifié n'existe pas : $TargetDirectory" -ForegroundColor Red
    exit
}

Write-Host "Recherche et suppression des dossiers bin et obj dans : $TargetDirectory" -ForegroundColor Cyan

# Recherche des dossiers bin et obj
$folders = Get-ChildItem -Path $TargetDirectory -Recurse -Directory -Include "bin", "obj"

if ($folders.Count -eq 0) {
    Write-Host "Aucun dossier bin ou obj trouvé." -ForegroundColor Yellow
    return
}

foreach ($folder in $folders) {
    try {
        Write-Host "Suppression : $($folder.FullName)" -ForegroundColor Yellow
        Remove-Item -Path $folder.FullName -Recurse -Force -ErrorAction Stop
    }
    catch {
        Write-Host "Erreur lors de la suppression de $($folder.FullName) : $_" -ForegroundColor Red
    }
}

Write-Host "Nettoyage terminé avec succès !" -ForegroundColor Green