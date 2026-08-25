# Tester les tests

Injecte des défauts plausibles dans le code de géométrie, un par un, et vérifie que
`Tools/GeometryTests` les rattrape.

```
run.bat            # ~4 min : 21 défauts, chacun compilé et confronté à la batterie
run.bat --verbose  # affiche la sortie de chaque essai
```

**Les fichiers source sont modifiés le temps de chaque essai puis restaurés**, y compris si on
interrompt le programme. Ne pas lancer sur un arbre de travail non commité — j'ai corrompu le
mien une fois, et c'est le détecteur de motif périmé qui l'a signalé plutôt que moi.

## Pourquoi

Une batterie de tests au vert ne dit rien tant qu'on ne l'a pas vue échouer. On peut avoir mille
assertions qui n'assertent rien. Le nombre de tests, le taux de couverture, le nombre de bugs déjà
trouvés : aucun de ces chiffres ne dit ce qui serait attrapé **la prochaine fois**.

Le taux de mutants tués, si. Un défaut injecté que les tests rattrapent est un mutant tué ; un
défaut qui passe est un survivant, et chaque survivant est un angle mort qu'on peut nommer.

## État actuel

**19 défauts sur 21 rattrapés (90 %).** Les deux survivants tiennent, sur les preuves
disponibles, à des mutations sans effet observable — le code qu'elles touchent est redondant.

La mesure est partie de 67 %. Les trois paliers :

| | |
|---|---|
| 67 % | mesure initiale, 7 survivants |
| 77 % | après avoir bouché deux trous réels et exercé la borne symétrique de `XAt` |
| 90 % | après suppression de l'ear-clipping devenu mort, et rattrapage en passe longue |

Ce que les survivants ont fait remonter, dans l'ordre où c'est arrivé :

- **Les UV pouvaient être écrasées sans que rien ne bronche.** Le test vérifiait que tous les
  sommets ne partageaient pas le *même* UV — les chants continuaient de varier, donc écraser
  celles de la face seule passait. C'est un défaut déjà survenu dans ce projet. Le test raisonne
  maintenant par triangle.
- **La marge de bord était dispensable.** Un trou qui affleure vraiment est déjà refusé par le
  test de croisement des arêtes ; il manquait le cas d'un trou à un demi-millimètre du bord, le
  seul que la marge attrape à elle seule.
- **`Triangulate` n'était plus appelé nulle part.** Un mutant qui survit sans explication mérite
  qu'on aille voir : celui-là frappait du code mort. 137 lignes supprimées.
- **La borne haute de `XAt` n'était pas couverte** alors que la borne basse l'était. Un cas de
  régression existant, retourné verticalement, a suffi.

## Le rattrapage en passe longue

Un défaut peut n'apparaître que sur une configuration rare. Avant de déclarer un survivant, le
programme lui laisse une seconde chance sur une passe treize fois plus longue.

Ce n'est pas une précaution théorique : la mutation « tolérance globale à zéro » survit à 600
panneaux et meurt à 8000. Sans ce rattrapage, elle serait comptée comme un angle mort et on
partirait écrire un test pour un cas déjà couvert.

## Ajouter un défaut

Une entrée dans `Catalogue.cs` : le fichier, le texte à remplacer, et une note disant ce que le
défaut casse. Le motif doit apparaître **exactement une fois** — sinon le programme le signale
comme périmé plutôt que de muter au hasard, et refuse de compter le résultat.

Les défauts ne sont pas tirés au sort. Muter au hasard produit surtout des programmes qui ne
compilent pas et des mutations équivalentes au code d'origine. Un catalogue écrit à la main
répond à une question plus utile : parmi les erreurs qu'on est réellement susceptible de commettre
sur cette géométrie, combien passeraient ?
