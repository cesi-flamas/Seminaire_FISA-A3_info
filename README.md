# Introduction
Ce projet est une proposition libre d'amélioration du séminaire de FISA A3 Info après les retours Qualigs palois de 2025. L'idée est de créer une cohésion entre les futurs ingénieurs généralistes et les futurs ingénieurs informatique sur un même projet. Un sentiment de désengagement s'est fait ressentir auprès des étudiants en informatiques qui ne comprenaient pas l'intérêt de refaire de la mécanique sur le projet du séminaire d'intégration alors qu'ils ont fait le choix de la spécialité informatique depuis la CPI A2.
Afin de palier ce découragement, je propose une solution open source utilisable par les Enseignants Responsable Pédagogique ou toutes celles et ceux en charge de l'animation de ce séminaire. L'objectif est de renforcer la cohésion entre élèves et de montrer que les métiers de l'industrie ont de nombreuses interactions.

# Sujet
## Description du projet «et pendant ce temps-là, les Shadoks pompaient...»
Vous sachant convaincus de l'urgence d'agir pour l'environnement, nous vous proposons de concevoir un engin fonctionnant à l'énergie humaine répondant à un besoin précis. Ce concept sera validé en partie grâce à la réalisation d'une maquette à échelle réduite.
Un concours en fin de projet permettra de voir les différentes approches choisies au sein de la promotion.

### Constat
Aujourd'hui, sur la plupart des chantiers de construction des engins de levage utilisant de l'énergie fossile sont utilisés. Indispensables sur de gros chantiers nécessitant de transporter de lourdes charges sur de grandes hauteurs, se pose la question d'utiliser l'énergie humaine sur des chantiers concernant des maisons individuelles de plain-pied. En effet, dans ce cas les charges et la hauteur à atteindre restent raisonnables même si nous sommes conscients qu'il ne s'agit pas du type d'habitat le plus vertueux ne serait-ce que part son emprise au sol.

### Enoncé
On vous propose donc de réfléchir à la conception d'un engin de levage utilisant l'énergie humaine qui puisse répondre à ces contraintes :
* L'engin doit être en mesure de lever une charge de 300 kg à la hauteur de 4 m et de la déplacer sur une distance de 3 m.
* Une personne seule doit être en mesure d'opérer l'engin seule en étant éventuellement guidée par une autre. Bien qu'utilisant l'énergie humaine, il est hors de question de revenir au Moyen-âge, la pénibilité du travail doit être prise en compte.
* Pour répondre, au cas d'usage envisagé, la conception doit être la plus simple possible permettant ainsi de faciliter le transport sur site (marché potentiel sur des zones difficiles d'accès où les réseaux routiers ou électriques ne sont pas installés), le montage et démontage (marché potentiel chez les particuliers avec le développement de l'autoconstruction), la mobilité sur le chantier.

Il est espéré que l'intérêt de la solution en termes d'empreinte écologique et d'accessibilité permettront de compenser le temps supplémentaire d'usage qu'induira la solution par rapport à un système utilisant l'énergie fossile.

### Réalisation attendue
Votre objectif sera donc de concevoir l'engin dans son ensemble pour réaliser une maquette à échelle réduite de la structure et valider le mécanisme (il ne pourra pas être à l'échelle dans votre maquette) en respectant le cahier des charges.
Vous devrez avoir aussi avoir défini un protocole de test permettant de valider que votre réalisation respecte les contraintes fixées.

# Liste du matériel et logiciels nécessaires
## Matériel nécessaire
La maquette de gestion de la grue nécessite le matériel suivant :
* Un plateau de support Arduino-servomoteur-seringues,
* Le système piston-poussoir en plastique PLA,
* Une carte Arduino Uno,
* Deux servomoteurs par mouvement de la grue, montés de part et d'autre du
  mécanisme, soit quatre pour le levage et l'orientation,
* Une alimentation 5–6 V capable de fournir **au moins 4 A** pour les quatre
  servomoteurs (bloc secteur ; comptez environ 1 A par servomoteur en charge),
* Une ”Breadboard”,
* Un câble USB, USB 2.0.
Le système de piston-poussoir PLA est imprimé ou à imprimer au Lab'CESI avec la coordination de vos ERP.

## IDE nécessaires
[Visual Studio](https://visualstudio.microsoft.com/fr/downloads/) pour l'édition des fichiers du projet WPF et pour la compilation de l'IHM.
[Arduino IDE](https://www.arduino.cc/en/software/) pour le téléversement du code source dans la carte Arduino Uno

# Montage du système Arduino
[Schéma du système Arduino connectant un servomoteur.](./servomotor_FISA-A3_integration_schema.png)

> **⚠️ Attention à l'alimentation.** Le schéma ci-dessus représente une pile 9 V
> (type 6LR61). **Ne l'utilisez pas :**
> * 9 V dépasse la tension nominale d'un servomoteur (4,8 à 6 V) ;
> * une pile de ce format ne délivre que quelques centaines de mA, alors qu'un
>   servomoteur en charge (pousser un piston de seringue) demande 1 à 2 A en
>   pointe.
>
> Le symptôme est toujours le même : le servomoteur tremble, bourdonne,
> n'atteint pas sa consigne, et la carte Arduino peut redémarrer toute seule.
> Utilisez un bloc secteur 5–6 V délivrant au moins 1 A par servomoteur.
>
> Le reste du câblage du schéma est correct. Le point à ne pas rater est la
> **masse commune** : le GND de l'Arduino et le (−) de l'alimentation doivent
> être reliés au même rail de la breadboard, sinon la carte et le servomoteur
> n'ont pas la même référence de tension et la commande est ignorée.

## Protocole de communication IHM ↔ carte
La liaison série est configurée à **9600 bauds, 8 bits, sans parité, 1 bit de
stop** des deux côtés.

Chaque **mouvement** de la grue est numéroté à partir de 1 et porte le nom de
la fonction qu'il commande :

| Mouvement | Broches | Nom | Sens |
|---|---|---|---|
| 1 | 9 et 10 | **Levage** | montée et descente de la charge, haut ↕ bas |
| 2 | 11 et 12 | **Orientation** | pivotement de la flèche, gauche ↔ droite |

Chaque mouvement est entraîné par **deux servomoteurs** montés de part et
d'autre du mécanisme, soit **quatre au total**.

### Messages échangés

| Sens | Message | Signification |
|---|---|---|
| IHM → carte | `S1:120\n` | met le mouvement 1 à 120° |
| IHM → carte | `120\n` | angle seul : s'applique au mouvement 1 |
| carte → IHM | `READY 2 4` | carte initialisée : 2 mouvements, 4 servomoteurs |
| carte → IHM | `OK 1 120` | consigne appliquée sur le mouvement 1 |
| carte → IHM | `ERR S5:90` | ligne invalide (mouvement inconnu, angle non numérique…), ignorée |

La carte ne répond **que lorsqu'une consigne change réellement**. Un même angle
renvoyé deux fois reste sans réponse : c'est volontaire, l'accusé systématique
saturait le tampon d'émission de 64 octets de la carte.

Au démarrage, la carte purge son tampon de réception avant d'écouter. Sans
cela, le bruit de la ligne série pendant le redémarrage suffit à composer un
nombre valide, et les servomoteurs partent tout seuls — observé en essai.

### Positions nommées
L'interface ne demande jamais de raisonner en degrés. Chaque mouvement déclare
ses positions remarquables, qui servent à la fois d'état affiché et de boutons
de rappel :

| Angle | Levage | Orientation |
|---|---|---|
| 15° | Entièrement descendu | Entièrement à gauche |
| 52° | Charge basse | Orientée à gauche |
| 90° | Charge à mi-hauteur | Flèche centrée |
| 128° | Charge haute | Orientée à droite |
| 165° | Entièrement monté | Entièrement à droite |

L'état affiché est la position **la plus proche** de l'angle courant : les
frontières tombent à mi-chemin entre deux repères, ce qui évite d'annoncer
« Entièrement monté » alors que la course n'est qu'aux trois quarts. L'angle
exact et le pourcentage de course restent affichés en dessous, en petit.

Ces positions se déclarent dans `BuildChannels()`. En ajouter une suffit à
créer son bouton de rappel : il n'y a pas de XAML à toucher.

### Paires montées en opposition
Montés face à face, les deux servomoteurs d'une paire doivent tourner **en sens
contraire** pour entraîner la charge dans le même sens : le second reçoit donc
l'angle en **miroir** du premier (15° ↔ 165°, 90° reste 90°). Leur donner la
même consigne les ferait se combattre, forcer et chauffer jusqu'à la casse.
`MOVEMENT_MIRRORED` vaut `true` par mouvement ; passez une paire à `false` si
elle est finalement montée dans le même sens.

Ce miroir est calculé **par la carte**, pas par l'IHM, et ce choix est
volontaire. L'interface n'émet qu'une consigne par mouvement, et la carte écrit
les deux servomoteurs dans la même instruction. Si l'IHM les pilotait
séparément, le tourniquet d'émission les décalerait de 50 ms et les deux
moitiés d'une paire se combattraient à chaque déplacement du curseur.

### Ajouter un mouvement
Le nombre de mouvements est fixé à **2**. Pour en ajouter un, trois
modifications, qui doivent rester cohérentes entre elles :

1. `MOVEMENT_COUNT` incrémenté dans le sketch ;
2. une ligne de plus dans `MOVEMENT_PINS` avec ses broches, et une entrée de
   plus dans `MOVEMENT_MIRRORED` ;
3. un bloc de plus dans `BuildChannels()` côté IHM, avec ses propres positions
   nommées.

L'interface crée son bandeau et ses boutons de rappel toute seule : il n'y a
pas de XAML à toucher. Les déclarations concernées :

| Fichier | Déclaration |
|---|---|
| [Arduino_servomotor_controller.ino](./Arduino_servomotor_controller.ino) | `MOVEMENT_COUNT`, `MOVEMENT_PINS` et `MOVEMENT_MIRRORED` |
| [MainWindow.xaml.cs](./Seminaire_FISA-A3_servomotor-controller/MainWindow.xaml.cs) | `BuildChannels()` (broches, miroir, nom, positions) |

La bibliothèque `Servo` gère jusqu'à douze servomoteurs sur une Uno,
mais **l'alimentation limite bien avant** : comptez environ 1 A par microservo
en charge.

### Cadence d'émission
L'IHM n'émet qu'**une commande toutes les 50 ms**, et **une seule voie à la
fois**, en tourniquet entre les voies qui ont changé. Ce n'est pas une
limitation arbitraire : à 9600 bauds on dispose de 960 octets/s, et émettre
toutes les voies à chaque tick ferait repasser commandes et accusés au dessus
de cette limite. Mesuré sur carte : 51 positions espacées de 50 ms sont reçues
sans aucune perte, alors que les 151 positions d'un balayage envoyé d'un bloc
saturent le tampon de réception et en perdent près d'une sur deux.

Comme on ne déplace qu'un curseur à la fois à la souris, le tourniquet ne se
voit pas à l'usage. Si vous ajoutez des voies et que vous voulez les piloter
simultanément, montez la vitesse de liaison des deux côtés plutôt que de
baisser ce délai.

Le débattement est limité à **15–165°** dans le sketch comme dans l'IHM : en
deçà et au delà, la plupart des servomoteurs arrivent en butée mécanique et
forcent. Si vous modifiez ces bornes, modifiez-les **aux deux endroits**
(`ANGLE_MIN` / `ANGLE_MAX` dans [Arduino_servomotor_controller.ino](./Arduino_servomotor_controller.ino),
`AngleMin` / `AngleMax` dans [ServoChannel.cs](./Seminaire_FISA-A3_servomotor-controller/ServoChannel.cs)),
faute de quoi l'écran affichera un angle que le servomoteur n'atteint jamais.

## L'interface
Le pupitre est conçu pour être utilisé sans connaître le protocole ni les
angles.

* **Voyant d'état**, en haut à droite : gris hors ligne, orange pendant le
  démarrage de la carte, vert quand les commandes sont actives, rouge en
  défaut. Un curseur déplacé voyant gris n'agit sur rien.
* **Un bandeau par mouvement**, avec son nom métier, l'état courant en clair
  (« Entièrement monté »), et l'angle exact en second plan.
* **Boutons de rappel** : un clic amène le mouvement à une position nommée,
  sans viser au degré près avec la souris. Le curseur reste disponible pour
  les positions intermédiaires, au clavier avec les flèches une fois
  sélectionné.
* **Repères d'extrémité** sous chaque curseur, pour que le sens du mouvement
  se lise sans manipuler.
* **« Tout au point neutre »** ramène les mouvements à mi-course, avant un
  démontage ou un transport.
* **Journal traduit** : la carte répond `OK 2 165`, l'interface affiche
  `Levage : Entièrement monté (165°)`.
* **Contrôle de cohérence** : si le sketch téléversé déclare un nombre de
  mouvements différent de celui de l'interface, le voyant passe au rouge et le
  journal l'annonce. C'est la cause typique d'un mouvement qui ne répond pas,
  et elle est invisible autrement.

## Mise en route
1. Téléverser [Arduino_servomotor_controller.ino](./Arduino_servomotor_controller.ino) sur la carte via l'IDE Arduino.
2. **Fermer le moniteur série de l'IDE Arduino** : il occupe le port COM et
   l'IHM ne pourra pas s'y connecter (« Accès refusé »).
3. Lancer l'IHM, choisir le port COM de la carte (bouton « Rafraîchir » si la
   carte a été branchée après le lancement) puis cliquer sur « Se connecter ».
4. Attendre le message `Carte prête` : l'ouverture du port peut redémarrer
   l'Arduino, le bootloader occupe alors la carte pendant environ 2 secondes.
   L'IHM transmet ensuite la position de chaque voie, ce qui synchronise les
   servomoteurs avec les curseurs affichés.
5. Déplacer les curseurs, un par servomoteur. Le journal en bas de fenêtre
   affiche les réponses de la carte, sous la forme `OK <voie> <angle>`.

Une seule application peut occuper le port à la fois : pensez à cliquer sur
« Déconnecter » avant de téléverser une nouvelle version du sketch.

# Montage du système de piston
