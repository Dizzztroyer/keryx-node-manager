# Keryx Node Manager

*Lire dans une autre langue : [English](README.md) · [Русский](README.ru.md) · [Español](README.es.md) · [Deutsch](README.de.md)*

Une application Windows pour gérer un nœud Keryx et un mineur GPU depuis une seule fenêtre — sans
manipulation manuelle de PowerShell/WSL/Docker. Outil communautaire, pas un produit officiel de
Keryx Labs.

## Télécharger et installer

Rendez-vous sur la page **[Releases](https://github.com/Dizzztroyer/keryx-node-manager/releases/latest)**
et téléchargez l'un des deux fichiers suivants :

- **`KeryxNodeManager-Setup-X.Y.Z.exe`** — installateur classique. Lancez-le, suivez l'assistant,
  c'est terminé. Un raccourci est ajouté sur le bureau et dans le menu Démarrer. Aucun droit
  administrateur requis.
- **`KeryxNodeManager-Portable-X.Y.Z.zip`** — version portable sans installation. Décompressez
  où vous voulez et lancez `KeryxNodeManager.exe`.

Au premier lancement, un assistant de configuration vous guide à travers une vérification du
système, la saisie de votre adresse de minage et la création/sélection d'un profil — le Tableau
de bord s'ouvre ensuite directement, avec le nœud et le mineur déjà en cours de démarrage.

**Prérequis :** Windows 10/11 x64, une carte GPU NVIDIA (pour la détection automatique et
l'overclocking). Le binaire du nœud (`keryxd.exe`) et celui du mineur (`keryx-miner.exe`) ne sont
pas inclus dans l'installateur lui-même, mais l'application les télécharge et les installe
automatiquement dès que vous en avez besoin pour la première fois — pas de page de mises à jour
séparée à consulter, ni de chemin à saisir manuellement.

## Fonctionnalités

- Le Tableau de bord affiche le statut du nœud, du mineur et des GPU au même endroit, avec un
  bouton unique « Tout démarrer / Tout arrêter » pour le nœud et le mineur ensemble, plus une
  icône dans la barre système avec un statut en direct.
- Détection automatique des GPU, attribution automatique du palier de minage selon la VRAM, ou
  sélection manuelle par carte.
- Overclocking GPU (cœur/mémoire) et contrôle du ventilateur — protégés par une boîte de
  confirmation.
- Téléchargement des modèles officiels en un clic (HTTP + miroirs torrent), avec une option
  manuelle (reprenable, avec vérification d'intégrité) en secours.
- Répertoire de nœuds publics et découverte automatique de nœuds voisins via votre propre nœud ;
  bascule vers un nœud de secours pendant la synchronisation du vôtre, avec retour automatique
  une fois celui-ci à jour.
- Téléchargement et extraction du data-dir en un clic (lien direct ou torrent).
- Journaux avec masquage automatique des secrets, export de diagnostic.
- Protection contre la surchauffe, option de démarrage automatique avec Windows.
- Plusieurs profils, interface disponible en 6 langues (ru/en/es/it/fr/uk).
- Vérificateur de mises à jour intégré pour le nœud et le mineur.

## Sécurité

L'application ne demande ni ne stocke jamais de phrase de récupération ou de clé privée. Toute
adresse RPC sur laquelle l'application peut répondre est liée uniquement à `127.0.0.1`
(localhost) — rien n'est exposé vers l'extérieur. Voir `docs/SECURITY.md` dans le dépôt pour plus
de détails.

## Pour les développeurs

```powershell
dotnet restore
dotnet test tests\KeryxNodeManager.Core.Tests\KeryxNodeManager.Core.Tests.csproj -c Release
dotnet run --project src\KeryxNodeManager.App -- --mock
```

`--mock` exécute l'interface avec des GPU virtuels, sans binaires Keryx réels ni NVAPI — un moyen
sûr de prévisualiser l'interface. Voir `docs/BUILD.md` pour les détails de compilation et
`docs/RELEASE.md` pour le processus de publication.

## Licence et statut

Projet en développement actif, initiative communautaire. Rapports de bugs et suggestions
bienvenus via les Issues.
