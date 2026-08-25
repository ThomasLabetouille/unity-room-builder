# Tests de propriétés — géométrie de découpe

Vérifie la géométrie de l'outil de découpe en dehors d'Unity, par milliers de cas :
`Assets/Editor/PolygonTriangulator.cs` (la triangulation 2D) et
`Assets/Editor/PanelMeshBuilder.cs` (l'assemblage 3D).

```
run.bat                        # 5000 panneaux tirés au hasard
run.bat --iterations 500       # passe courte, pour la boucle d'édition
run.bat --iterations 100000    # passe longue, avant de livrer au level designer
run.bat --seed 12345           # rejoue une exécution à l'identique
```

Code de sortie 0 si tout passe, 1 sinon — utilisable tel quel dans un hook de commit.

Ordre de grandeur sous Mono : 500 panneaux en 1,4 s, 5000 en 11 s, 30 000 en 1 min. Sous
.NET 8 (ce que lance `run.bat`) compte deux à trois fois moins.

## Pourquoi hors d'Unity

Ni `PolygonTriangulator` ni `PanelMeshBuilder` ne dépendent d'une API Unity : uniquement
`Vector2`, `Vector3` et `Mathf`, dont `UnityShims.cs` fournit des doublures d'une centaine de
lignes. `RoomBuilderWindow` ne garde que ce qu'Unity seul peut faire — écrire l'asset, recuire le
collider, inscrire l'undo. Résultat : la boucle de test
tourne en quelques secondes au lieu d'un aller-retour dans l'éditeur, et on peut se permettre
100 000 cas.

Si un jour la triangulation se met à utiliser un type Unity absent des doublures, ce projet ne
compile plus. C'est voulu : c'est le garde-fou qui maintient la géométrie séparée de l'éditeur.

Le `.csproj` compile **le** fichier du projet Unity, pas une copie.

Ce dossier doit rester **hors de `Assets/`**, sinon Unity compilerait les doublures en double avec
les vrais types Unity.

## Ce qui est vérifié

Des propriétés, pas des résultats figés. Chacune doit tenir pour n'importe quelle entrée :

| | |
|---|---|
| **P1** | aire des triangles = aire du panneau − aire des trous |
| **P2** | aucun triangle plat (seuil proportionnel à la taille du panneau) |
| **P3** | un point dans la matière est couvert par exactement un triangle, un point dans un trou par aucun |
| **P4** | surface étanche : chaque arête intérieure est partagée par exactement deux triangles, chaque arête solitaire longe bien un contour (une T-jonction laisse une fissure d'un pixel au rendu) |
| **P5** | aucun sommet hors du panneau ni à l'intérieur d'un trou |
| **P6** | `SubdivideLoop` ajoute des sommets sans déformer le contour ni en perdre |
| **P7** | deux appels identiques rendent exactement la même chose |
| **P8** | la surface reste correcte à l'échelle ×8 et ×0.25 — vérifiée sur un tirage sur huit, l'indépendance à l'échelle étant une propriété de l'algorithme et pas d'un panneau donné |

Et sur l'assemblage 3D :

| | |
|---|---|
| **M0** | le repérage des axes désigne bien le plus petit côté comme épaisseur |
| **M2** | chaque triangle est émis exactement deux fois, en enroulements opposés (le doublage recto/verso) |
| **M3** | le maillage dédoublé est un prisme fermé, orientable, de volume égal à aire × épaisseur |
| **M4** | la géométrie tient dans la boîte du panneau **et la remplit** — un panneau qui ne toucherait pas ses propres bords trahirait une échelle perdue en route |
| **M5** | UV dans [0,1] et non tous identiques (sinon la texture se réduit à un aplat) |
| **M6** | déterminisme |

Les trois repérages d'axes sont exercés — épaisseur sur Z (mur), sur Y (sol / plafond) et sur X —
sinon une interversion ne serait détectée que sur les murs.

Plus les garde-fous de la découpe (`IsSimplePolygon`, `PolygonsOverlap`, `PointInPolygon`), qui
décident si un contour dessiné est acceptable : un faux négatif laisse passer une géométrie
invalide, un faux positif refuse une découpe légitime sous les yeux du level designer.

## Est-ce que ces tests valent quelque chose ?

`Tools/MutationTests` répond par un chiffre : il injecte des défauts plausibles dans la géométrie,
un par un, et compte combien cette batterie en rattrape. **19 sur 21 aujourd'hui.**

Trois des propriétés listées ci-dessus existent parce qu'un défaut injecté passait sans être vu.

## Quand un test casse

Le fuzz réimprime le cas en C# collable. On le met dans `Regressions.cs`, **on vérifie qu'il est
bien rouge sur la version buguée**, on corrige, on vérifie qu'il passe au vert. Le cas reste là
pour toujours.

L'étape en gras n'est pas décorative. Le premier cas figé ajouté ici ne reproduisait pas le défaut
qu'il était censé garder : corriger un autre bug avait suffi à le faire disparaître, et personne
ne s'en serait aperçu. Un cas de régression qu'on n'a pas vu échouer ne prouve rien — il occupe
juste de la place et donne une fausse assurance.

`Regressions.cs` contient déjà les configurations qui ont mis en défaut les versions précédentes,
notamment les trous alignés en Y — celles qui faisaient échouer l'ancien ear-clipping avec ponts
alors que tous les cas écrits à la main passaient.

## Ce que le harnais a déjà trouvé

Deux défauts réels, dès les premières exécutions, tous deux corrigés dans
`PolygonTriangulator.cs` :

- **Seuil de dégénérescence absolu.** Le filtre des triangles plats comparait à `1e-9` en dur.
  Un même mur rendait donc un triangle de plus ou de moins selon sa position et son échelle.
  Le seuil est devenu proportionnel à la taille du panneau.
- **Extrapolation non bornée.** `Edge.XAt` était évalué jusqu'à un `Epsilon` au-delà de l'étendue
  de l'arête. Sur une arête à forte pente, ce décalage minuscule en Y se traduisait par 1e-3 en X
  — assez pour poser un sommet à l'intérieur d'un trou voisin. `y` est maintenant borné.

Les deux cas sont figés dans `Regressions.cs`, chacun vérifié rouge sur la version qui portait le
défaut et vert sur la version corrigée.

## Contrat

Un panneau est un **contour plat extrudé sur une épaisseur**. Ce contour n'a pas à être
rectangulaire : un mur donne son rectangle, un sol ou un plafond dessiné à main levée donne sa
forme libre. Les deux passent par le même chemin et le même banc de test — le générateur tire un
contour non rectangulaire un panneau sur quatre.

La découpe exige un contour de trou **strictement intérieur** au panneau. Un trou affleurant le bord
fusionnerait avec le contour extérieur, et le chant généré autour du trou ferait double emploi
avec celui du panneau. Conséquence pratique à connaître : on ne peut pas percer une porte qui
descend jusqu'au sol, il reste toujours un seuil.

Les trous doivent aussi être disjoints, ce que la découpe vérifie de son côté en refusant un
contour qui en chevauche un autre.

## Limites connues

La tolérance de `PolygonTriangulator` (`Epsilon = 1e-5`) est exprimée en unités monde : deux
points à moins de 10 µm sont le même point. C'est le bon choix pour du level design, mais ça
implique deux choses.

Le découpage en tranches peut différer légèrement selon la taille du panneau — c'est pour ça que
P8 vérifie la surface et pas le nombre de triangles.

Et au-delà d'une centaine d'unités depuis l'origine du panneau, 1e-5 passe sous la précision du
`float` 32 bits. Sans objet en pratique : `RebuildPanelMesh` triangule toujours en coordonnées
centrées sur le pivot, donc en ±halfU / ±halfV. Un mur de 8 m travaille dans ±4. Ne pas ajouter de
test de translation : il serait rouge sur un scénario que l'outil ne produit jamais.

P4 est écartée sur les panneaux dont deux sommets sont distants de moins de 5e-5 — le
triangulateur ne les distingue plus à sa propre tolérance, et la notion d'étanchéité y perd son
sens. Ça arrive sur environ 1 % des tirages, et le nombre de cas écartés est affiché à chaque
exécution : si ce chiffre grimpe, c'est que le générateur produit de la géométrie de plus en plus
dégénérée, et il faut aller voir.

M3 est écartée sur les panneaux dont la face se pince — deux portions de bord s'y rejoignent en un
point, ce qui arrive sur les tranches très minces des panneaux à beaucoup de trous. Le maillage
reste étanche, il cesse seulement d'être une variété au sens strict. Environ 2 pour mille, compté
et affiché comme le reste.

Dernier repère : 40 000 panneaux passent l'ensemble de la batterie, contours libres compris.

## Si ça devient lent

Les deux postes de coût sont connus. P3 confronte chaque point échantillonné aux triangles
susceptibles de le contenir — d'où le rangement par bandes en Y, sans lequel elle représentait à
elle seule 90 % de la durée. P8 rejoue toute la batterie sur deux variantes, d'où
l'échantillonnage à un tirage sur huit. Les deux réglages sont en haut de `Invariants.cs` et de
`Program.cs`.

Avant de toucher à l'un ou l'autre : rendre le harnais plus rapide n'a d'intérêt que si la lenteur
t'empêche de le lancer. À quelques secondes, la question ne se pose plus.
