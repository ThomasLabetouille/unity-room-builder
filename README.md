# LevelDesignTools

Outils d'éditeur Unity pour blockouter des niveaux : générer des salles, dessiner un contour au
sol et l'extruder, percer des ouvertures dans les murs. Unity 6000.5, URP.

Le code de l'outil est écrit en grande partie par un agent Claude piloté en MCP depuis l'éditeur.
Ce dépôt contient donc surtout ce qui vérifie ce que l'agent produit, parce que c'est là que le
travail se trouve : du code qui compile et qui a l'air de marcher n'est pas du code correct.

## Ce que fait l'outil

`Tools > Level Design > Room Builder`, trois modes.

**Boîte** — une salle rectangulaire à partir de dimensions : quatre murs, un sol, un plafond.

**Dessin libre** — on clique un contour dans la vue Scene, on le ferme, la boîte apparaît et sa
hauteur suit la souris jusqu'au clic de validation. Ctrl aimante sur la grille, tirer sous le plan
de dessin extrude vers le bas.

**Découpe** — on survole une surface, on dessine le contour d'une ouverture dessus, elle est
percée. Porte, fenêtre, trémie. Les trous s'accumulent : un mur reste découpable indéfiniment.

La découpe ne retire pas de matière au maillage. Elle garde la description logique du panneau — son
contour, son épaisseur, la liste de ses trous — sur un composant `RoomPanel`, et régénère le
maillage complet à chaque fois. C'est ce qui permet de percer vingt ouvertures sans dégradation,
d'en retirer une, ou d'annuler.

Elle accepte tout ce qui est un contour plat extrudé : les murs, les sols et plafonds à main levée,
et n'importe quelle boîte, y compris venue d'ailleurs. Un cylindre ou une sphère sont refusés avec
la raison — appliquée à une forme quelconque, la reconstruction la remplacerait par une boîte.

## Lancer les vérifications

```
Tools\GeometryTests\run.bat     # ~10 s : propriétés géométriques sur des milliers de cas
Tools\MutationTests\run.bat     # ~4 min : injecte des défauts, vérifie qu'ils sont rattrapés
```

Il faut le SDK .NET. Les deux rendent 0 si tout passe, 1 sinon, donc utilisables dans un hook de
commit.

## Comment la géométrie est vérifiée

La triangulation et l'assemblage 3D ne dépendent d'aucune API Unity — uniquement `Vector2`,
`Vector3` et `Mathf`, dont une centaine de lignes de doublures suffit à tenir lieu. `GeometryTests`
compile donc **les fichiers du projet**, pas des copies, et les exerce hors de l'éditeur. Une passe
de 5000 panneaux prend quelques secondes au lieu d'un aller-retour dans Unity.

Ce qui est vérifié n'est pas une liste de résultats figés mais des propriétés qui doivent tenir
pour n'importe quelle entrée : l'aire des triangles égale l'aire du panneau moins celle des trous,
un point dans la matière est couvert par exactement un triangle et un point dans un trou par aucun,
le maillage dédoublé est un prisme fermé de volume égal à aire × épaisseur, aucun triangle ne se
replie sur un seul texel, la surface est identique à un second appel.

40 000 panneaux passent aujourd'hui, jusqu'à 21 ouvertures chacun. Les cas tirés au hasard sont
complétés par des cas figés dans `Regressions.cs` — chacun est un
défaut qui a réellement existé, et chacun a été vu échouer sur la version qui le portait. Un cas de
régression qu'on n'a jamais vu rouge ne garantit rien ; j'en ai ajouté un sans le vérifier et il ne
reproduisait déjà plus rien.

`MutationTests` mesure ce que tout ça vaut. Il injecte 21 défauts plausibles dans la géométrie, un
par un, et compte combien la batterie en rattrape : **19 sur 21**. Les deux survivants frappent du
code dont rien ne dépend. La mesure est partie de 67 % ; les trois propriétés ajoutées depuis
existent parce qu'un défaut injecté passait sans être vu.

## Ce que ça a trouvé

Dans du code qui compilait et fonctionnait à l'écran :

- Un seuil de dégénérescence en dur (`1e-9`) là où il fallait du relatif. Le même mur rendait un
  triangle de plus ou de moins selon sa position dans le niveau et son échelle.
- Une extrapolation non bornée dans le calcul des intersections. Sur une arête à forte pente — deux
  clics séparés de 2,8 mm en Y pour 28 cm en X — un décalage d'un epsilon se traduisait par 1 mm,
  assez pour poser un sommet à l'intérieur d'un trou voisin. Un cas sur 1500.
- Le chant du mur et sa face étaient calculés en parallèle et divergeaient quand deux sommets de
  trous voisins avaient des ordonnées à moins de 10 µm. Fissure de 15 µm tout autour du trou. Le
  chant est maintenant extrudé du bord de la face, la question ne se pose plus.
- Le cylindre d'Unity passait le test « est-ce une boîte ? ». Tous ses sommets reposent sur les
  plans du haut et du bas, et le test raisonnait sommet par sommet. Le découper l'aurait remplacé
  par une boîte. Celui-là, aucun test hors moteur ne pouvait le voir : il a fallu interroger
  l'éditeur en direct.
- 137 lignes d'ear-clipping que plus rien n'appelait. Un mutant qui survivait sans explication.

Trois fois, l'inverse s'est produit : le harnais a crié sur du code correct. Un point échantillonné
tombait pile sur la frontière entre deux tranches et était compté des deux côtés ; une détection de
T-jonctions par colinéarité se déclenchait sur des triangles très aplatis, où un sommet à 1,6 µm
d'une arête de 2,7 m n'est pas dessus ; une tolérance de soudure plus fine que celle du code
signalait des fissures que le code confond volontairement. Calibrer les propriétés sur le contrat
réel du code représente une bonne part du travail, et un harnais qui crie au loup est abandonné en
trois jours.

## Organisation

```
Assets/Editor/
  RoomBuilderWindow.cs      fenêtre d'éditeur, interaction vue Scene, écriture des assets
  PanelMeshBuilder.cs       assemblage 3D d'un panneau — sans API Unity
  PolygonTriangulator.cs    triangulation d'un contour percé de trous — sans API Unity
Assets/LevelDesignTools/
  RoomPanel.cs              état logique d'un panneau, sérialisé avec la scène
Tools/GeometryTests/        propriétés, générateurs, cas de régression
Tools/MutationTests/        injection de défauts et taux de rattrapage
```

La séparation entre les deux premiers fichiers et le troisième n'est pas cosmétique : dès qu'une
ligne de géométrie dépend de `MeshFilter` ou de `Undo`, elle n'est plus testable qu'en lançant
l'éditeur, et une boucle de test à 40 secondes cesse d'être une boucle de test. Les doublures de
`GeometryTests` sont le garde-fou : si la géométrie se met à utiliser un type Unity absent, le
projet de test ne compile plus.

## Limites actuelles

La découpe reconstruit un panneau plat extrudé. Elle ne sait pas soustraire de la matière à un
maillage quelconque — un prop importé, un cylindre. Ce serait une opération booléenne, c'est-à-dire
une autre implémentation, pas un élargissement de celle-ci.

Le contour d'un trou doit être strictement intérieur au panneau. Conséquence pratique : on ne peut
pas percer une porte qui descend jusqu'au sol, il reste toujours un seuil.

Chaque triangle est émis deux fois, recto et verso, pour ne dépendre d'aucune convention
d'enroulement. Un mur à une ouverture pèse 432 sommets là où une cinquantaine suffirait. Les tests
établissent qu'une orientation cohérente existe, mais celle qui est émise n'en est pas une : sur
1498 panneaux, aucun n'a un enroulement déjà cohérent, jusqu'à 28 % des triangles seraient à
retourner. Supprimer le doublage demande donc d'orienter le maillage à l'émission, pas de retirer
quatre lignes.

L'étanchéité n'est pas vérifiée sur les panneaux dont la face se pince en un point, ni la propriété
de T-jonction sur ceux dont deux sommets sont plus proches que la tolérance du triangulateur. Le
nombre de cas écartés est affiché à chaque exécution.
