# Prise de notes

Petite application de prise de notes en **thème sombre**, pour Windows, avec mise en forme riche et
**onglets par note**. Aucune base de données : chaque note est un fichier JSON lisible et éditable à la main.

## Télécharger

[**Télécharger PriseDeNotes**](https://github.com/jt-apps-lab/PriseDeNotes/releases/latest)
— dernière version pour Windows : télécharge le `.zip`, décompresse-le dans un dossier, puis lance `PriseDeNotes.exe`.
Nécessite le [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/fr-fr/download/dotnet/8.0).

## Fonctionnalités

- **Notes** — liste des titres à gauche, avec glisser-déposer pour réordonner, crayon pour renommer,
  corbeille pour supprimer, et info-bulle au survol (dates, caractères, mots, réglages).
- **Onglets par note** — chaque note est découpée en onglets, chacun avec son propre texte :
  le « **+ »** à droite de la barre ajoute un onglet, un **clic** l'affiche, un **glisser-déposer** le déplace,
  un **clic droit** ouvre le menu (nouvel onglet, renommer, supprimer, déplacer).
  Un titre en cours de saisie se valide par **Entrée** ou en cliquant ailleurs, et s'annule par **Échap**.
  Le dernier onglet d'une note ne se supprime pas. Police et couleurs sont partagées par toute la note.
- **Mise en forme riche** — gras, italique, souligné, barré, alignement à gauche / centré / à droite,
  police et taille. Avec une sélection, le réglage ne touche que la sélection ; sans sélection, il s'applique à toute la note.
- **Couleurs** — fond de la note et couleur de police choisis par note, repris dans la liste et les onglets.
- **Recherche** — filtre la liste sur le titre et le contenu de **tous les onglets**.
- **Vue « Voir tout »** — toutes les notes en cartes, tri par lignes ou par colonnes, recherche dans les titres et les textes.
- **Archiver et restaurer** — dans « Voir tout », le bouton **Archiver** propose *Archiver toutes les notes*
  ou *Passer en mode sélection* ; on y entre aussi par un **appui long** sur une carte (cartes entourées de bleu,
  coche en haut à droite), puis un **clic** ajoute ou retire une note et **Archiver (n)** emballe le choix.
  L'archive est un zip proposé à côté de l'exe et enregistrable ailleurs ; « Restaurer » relit un zip
  ou un fichier `.json` : les notes importées prennent un identifiant neuf et n'écrasent jamais une note existante.
  `Échap` quitte le mode sélection.
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
| `Ctrl` + `F` | recherche |
| `Ctrl` + `T` | nouvel onglet |

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

Une **archive** créée par « Archiver » contient un fichier `.json` par note, nommé d'après son titre :
même format que ci-dessus, donc lisible et éditable à la main. « Restaurer » relit ces archives,
ou des fichiers `.json` pris un par un ; chaque note importée reçoit un identifiant neuf.

Un fichier illisible est ignoré au chargement et jamais supprimé ; le nombre de fichiers illisibles est
signalé dans la barre d'état. Chaque enregistrement passe par un fichier temporaire `.tmp` remplacé
d'un coup : une coupure pendant l'écriture laisse la version précédente intacte.
Une seule fenêtre de l'application peut être ouverte à la fois.

## Architecture

| Fichier | Rôle |
| --- | --- |
| `Program.cs` | point d'entrée, mode diagnostic `--diag` |
| `MainForm.cs` | fenêtre principale : barre d'outils, liste, éditeur, onglets, enregistrement |
| `TabStrip.cs` | barre d'onglets dessinée à la main (ajout, renommage en place, glisser-déposer, menu) |
| `TitleListBox.cs` | liste des titres dessinée à la main (poignée, crayon, corbeille, glisser-déposer) |
| `Note.cs` / `NoteStore.cs` | modèle note + onglets, persistance JSON |
| `NotesOverviewForm.cs` | vue « Voir tout », sélection par appui long, archive et restauration |
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
