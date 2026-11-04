# DeskNest (v0.7.0.1)

Application Windows de rangement du bureau : créez des zones pour regrouper vos raccourcis, fichiers et dossiers.
Cible : Windows 10 / 11, C# / WPF (.NET 8).

## Compiler et installer
1. Installer le SDK .NET 8 : `winget install Microsoft.DotNet.SDK.8`
2. (Optionnel, pour l'installateur) installer Inno Setup 6 : `winget install JRSoftware.InnoSetup`
3. Double-cliquer sur `build.bat`
   - `publish\DeskNest.exe` : exécutable autonome
   - `installer\DeskNest-Setup.exe` : installateur (sans droits administrateur)

Journal de diagnostic (bureau, « Afficher le bureau ») : `%APPDATA%\DeskNest\diagnostic.log`

Pour tester rapidement : `dotnet run`

## Utilisation
- Redimensionner : tirer sur n'importe quel côté ou coin de la zone.
- Clic droit sur une zone : renommer, Folder Portal, couleur, opacité, taille des icônes, titre, Roll Up, verrouillage, suppression.
- Glisser des fichiers/dossiers/raccourcis depuis l'Explorateur ou le bureau : ils sont ajoutés comme références (jamais déplacés).
  Un élément venant du bureau est rangé dans `%USERPROFILE%\DeskNest\Items` (il disparaît du bureau) ; « Retirer de la zone » le remet à sa place d'origine. Mode alternatif : seulement le cacher.
- Sélection multiple : clic, Ctrl+clic, Maj+clic, rectangle de sélection dans une zone vide, Ctrl+A. On déplace (glisser) ou on retire (Suppr, clic droit) toute la sélection d'un coup.
- Tri personnalisé : glisser un élément à l'intérieur de la zone, un trait indique où il sera inséré.
- Glisser un élément vers une autre zone pour l'y transférer.
- Folder Portal : une zone affiche le contenu d'un dossier (double-clic sur un dossier pour y entrer, « .. » pour remonter).
- Titre : bouton ☰ à gauche (même menu que le clic droit), icône de la zone (grille = zone normale, dossier = Folder Portal),
  double-clic sur le texte du titre pour le modifier (Entrée valide, Échap annule), double-clic ailleurs sur la barre : réduire / développer (Roll Up).
- Menu des éléments (clic droit) : Ouvrir, Ouvrir avec…, Emplacement du fichier, Couper, Copier, Copier le chemin, Coller, Renommer, Supprimer (Corbeille), Retirer de la zone, Propriétés. Raccourcis : Ctrl+C / Ctrl+X / Ctrl+V, F2, Suppr (zone classique : retire de la zone ; Folder Portal ou Maj+Suppr : Corbeille).
- Renommer un élément : clic sur son nom (quand il est déjà sélectionné), F2 ou menu. Entrée valide, Échap annule.
- Langues (Paramètres > Général > Langue) : English (par défaut), Français, Italiano, Deutsch, Español, Português, العربية, 中文, 日本語, 한국어. Le changement est immédiat.
- Polices : Paramètres > Zones (ou clic droit > Polices…) : police du titre et du nom des icônes, propres à chaque zone.
- Folder Portal : déposer un fichier ou un dossier dans la zone le déplace réellement dans le dossier affiché (Ctrl = copier ; au centre d'une icône de dossier = dans ce sous-dossier).
- Glisser une icône d'une zone vers le bureau ou une fenêtre de l'Explorateur : l'élément est déplacé là où on le dépose (Ctrl = copier).
- Développement au survol (Paramètres > Comportement, puis par zone) : une zone réduite s'ouvre quand la souris la survole et se réduit quand elle la quitte.
- Paramètres (icône de notification ou clic droit > Paramètres…) : activer/désactiver DeskNest, démarrage avec Windows, raccourcis, couleur / opacité / icônes par zone, valeurs par défaut, arrondi des coins, instantanés de disposition.
- Double-clic sur une zone vide du bureau, ou Ctrl + Alt + F, ou double-clic sur l'icône de notification : masquer / afficher les zones.
- Icône de notification : nouvelle zone, Folder Portal, lancement avec Windows, remettre toutes les icônes sur le bureau, quitter.

## Données
`%APPDATA%\DeskNest\layout.json` (+ sauvegarde quotidienne dans `backups\`, 14 conservées).

## Important
Si tu désinstalles l'application, utilise d'abord « Remettre toutes les icônes sur le bureau » (icône de notification),
sinon les éléments rangés resteront dans le dossier DeskNest\Items de votre profil (ils ne sont jamais supprimés).
