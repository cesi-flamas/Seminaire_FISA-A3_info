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
* Autant de servomoteurs que de seringues employés par la grue,
* Une alimentation 5–6 V capable de fournir **au moins 2 A** (bloc secteur, ou 4 piles AA en support),
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
> Utilisez un bloc secteur 5–6 V / 2 A ou 4 piles AA.
>
> Le reste du câblage du schéma est correct. Le point à ne pas rater est la
> **masse commune** : le GND de l'Arduino et le (−) de l'alimentation doivent
> être reliés au même rail de la breadboard, sinon la carte et le servomoteur
> n'ont pas la même référence de tension et la commande est ignorée.

## Protocole de communication IHM ↔ carte
La liaison série est configurée à **9600 bauds, 8 bits, sans parité, 1 bit de
stop** des deux côtés.

| Sens | Message | Signification |
|---|---|---|
| IHM → carte | `120\n` | consigne d'angle en degrés |
| carte → IHM | `READY 90` | carte initialisée, servomoteur à 90° |
| carte → IHM | `OK 120` | consigne appliquée |
| carte → IHM | `ERR abc` | ligne reçue invalide, ignorée |

Le débattement est limité à **15–165°** dans le sketch comme dans l'IHM : en
deçà et au delà, la plupart des servomoteurs arrivent en butée mécanique et
forcent. Si vous modifiez ces bornes, modifiez-les **aux deux endroits**
(`ANGLE_MIN` / `ANGLE_MAX` dans [Arduino_servomotor_controller.ino](./Arduino_servomotor_controller.ino),
`AngleMin` / `AngleMax` et les bornes du slider dans le projet WPF), faute de
quoi l'écran affichera un angle que le servomoteur n'atteint jamais.

## Mise en route
1. Téléverser [Arduino_servomotor_controller.ino](./Arduino_servomotor_controller.ino) sur la carte via l'IDE Arduino.
2. **Fermer le moniteur série de l'IDE Arduino** : il occupe le port COM et
   l'IHM ne pourra pas s'y connecter (« Accès refusé »).
3. Lancer l'IHM, choisir le port COM de la carte (bouton « Rafraîchir » si la
   carte a été branchée après le lancement) puis cliquer sur « Se connecter ».
4. Attendre le message `Carte prête` : l'ouverture du port redémarre l'Arduino,
   le bootloader occupe la carte pendant environ 2 secondes.
5. Déplacer le curseur. Le journal en bas de fenêtre affiche les réponses de la
   carte.

# Montage du système de piston
