# Prise de notes

Petite application de prise de notes en **thème sombre**, pour Windows, avec mise en forme riche et
**onglets par note**. Aucune base de données : chaque note est un fichier JSON lisible et éditable à la main.

## Fonctionnalités

- **Notes** — liste des titres à gauche, avec glisser-déposer pour réordonner, crayon pour renommer,
  corbeille pour supprimer, et info-bulle au survol (dates, caractères, mots, réglages).
- **Onglets par note** — chaque note est découpée en onglets, chacun avec son propre texte :
  le « **+ »** à droite de la barre ajoute un onglet, un **clic** l'affiche, un **double-clic** (ou F2) le renomme,
  un **glisser-déposer** le déplace, un **clic droit** ouvre le menu (nouvel onglet, renommer, supprimer, déplacer).
  Le dernier onglet d'une note ne se supprime pas. Police et couleurs sont partagées par toute la note.
- **Mise en forme riche** — gras, italique, souligné, barré, alignement à gauche / centré / à droite,
  police et taille. Avec une sélection, le réglage ne touche que la sélection ; sans sélection, il s'applique à toute la note.
- **Couleurs** — fond de la note et couleur de police choisis par note, repris dans la liste et les onglets.
- **Recherche** — filtre la liste sur le titre et le contenu de **tous les onglets**.
- **Vue « Voir tout »** — toutes les notes en cartes, tri par lignes ou par colonnes, recherche dans les titres et les textes.
- **Confort** — numéros de lignes, retour à la ligne automatique, enregistrement automatique,
  titre de note calculé automatiquement sur les premiers mots (dès qu'on le modifie à la main, il devient fixe),
  confirmation de suppression désactivable.
- **Affichage** — thème sombre appliqué partout, y compris la barre de titre Windows, et dessin adapté
  à la mise à l'échelle de l'écran (100 %, 150 %, 225 %…).

## Raccourcis

| Touche | Action |
| --- | --- |
| `Ctrl` + `N` | nouvelle note |
| `Ctrl` + `S` | enregistrer |
| `Ctrl` + `Suppr` ou `Ctrl` + `D` | supprimer la note |
| `Ctrl` + `F` | recherche |
| `Ctrl` + `T` | nouvel onglet |
| `Ctrl` + `W` | fermer l'onglet |
| `F2` | renommer l'onglet survolé |

## Compilation et exécution

Il faut le SDK **.NET 8** (cible `net8.0-windows`, Windows Forms).

```
dotnet build              # version de développement
dotnet build -c Release   # version distribuée : bin\Release\net8.0-windows\PriseDeNotes.exe
dotnet run
```

## Où sont les notes

Dans `%APPDATA%\PriseDeNotes\` :

- `notes\<id>.json` — une note par fichier : titre, onglets (titre, texte, texte enrichi RTF),
  police, couleurs, dates. Les fichiers d'avant les onglets sont migrés automatiquement :
  leur texte devient l'**onglet 1** de la note.
- `ui.json` — taille et position de la fenêtre, largeur de la colonne de gauche, réglages, ordre des notes.

Un fichier illisible est ignoré au chargement et jamais supprimé ; le nombre de fichiers illisibles est
signalé dans la barre d'état.

## Architecture

| Fichier | Rôle |
| --- | --- |
| `Program.cs` | point d'entrée, mode diagnostic `--diag` |
| `MainForm.cs` | fenêtre principale : barre d'outils, liste, éditeur, onglets, enregistrement |
| `TabStrip.cs` | barre d'onglets dessinée à la main (ajout, renommage en place, glisser-déposer, menu) |
| `TitleListBox.cs` | liste des titres dessinée à la main (poignée, crayon, corbeille, glisser-déposer) |
| `Note.cs` / `NoteStore.cs` | modèle note + onglets, persistance JSON |
| `NotesOverviewForm.cs` | vue « Voir tout » |
| `LineNumberGutter.cs` / `RichEditLineMetrics.cs` | numéros de lignes alignés sur les métriques réelles de RichEdit |
| `RichTextFormatting.cs` | styles et police appliqués par messages RichEdit |
| `Theme.cs` / `ColorButton.cs` / `DarkMenuRenderer.cs` / `DarkToolTip.cs` | palette sombre et contrôles assortis |

Les contrôles particuliers sont dessinés à la main et dimensionnés d'après les métriques de la police,
pour rester nets quelle que soit la mise à l'échelle Windows.

## Diagnostic de mise en page

```
dotnet bin\Debug\net8.0-windows\PriseDeNotes.dll --diag
```

Ouvre la fenêtre, écrit un rapport (DPI, largeurs disponibles et nécessaires pour chaque texte,
hauteurs de lignes, largeur des onglets) dans `%TMP%\prisedenotes_diag.txt`, puis se ferme.
