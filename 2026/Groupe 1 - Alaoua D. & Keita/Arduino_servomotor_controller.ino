/*
 * Pilotage des mouvements de la grue depuis l'IHM WPF
 * "Seminaire_FISA-A3_servomotor-controller".
 *
 * Chaque MOUVEMENT de la grue est entraine par un ou plusieurs servomoteurs.
 * Le montage comporte DEUX mouvements, le LEVAGE et l'ORIENTATION, chacun
 * entraine par DEUX servomoteurs, soit quatre au total.
 *
 * Quand une paire est montee face a face, ses deux servomoteurs doivent
 * tourner en sens contraire pour entrainer la charge dans le meme sens : le
 * second recoit alors l'angle en miroir du premier. Leur donner la meme
 * consigne les ferait se combattre, forcer et chauffer jusqu'a la casse.
 *
 * Ce miroir est applique ICI et non cote PC a dessein. L'IHM n'emet qu'une
 * commande toutes les 50 ms, en tourniquet sur les mouvements : si elle devait
 * piloter les deux servomoteurs d'une paire separement, les deux moities
 * bougeraient a 50 ms d'intervalle et se combattraient a chaque deplacement.
 * Traitees dans la meme instruction, elles restent exactement synchrones.
 *
 * Protocole serie (9600 bauds, 8N1) :
 *   PC -> carte : "S<mouvement>:<angle>\n", par exemple "S2:120\n"
 *                 "<angle>\n" seul est accepte et s'applique au mouvement 1
 *   carte -> PC : "READY <mouvements> <servomoteurs>"  au demarrage
 *                 "OK <mouvement> <angle>"   consigne acceptee
 *                 "DONE <mouvement> <angle>" consigne atteinte
 *                 "ERR <ligne>"              ligne recue invalide
 *
 * L'angle annonce est toujours celui du mouvement, pas celui d'un servomoteur
 * en particulier : c'est la consigne que l'operateur a demandee.
 *
 * La carte ne repond QUE sur changement de consigne : l'IHM emet en continu
 * pendant que l'on deplace un curseur, et un accuse par message saturait le
 * tampon d'emission de 64 octets, ce qui bloquait Serial.print() et faisait
 * decrocher les servomoteurs de plusieurs secondes.
 */

#include <Servo.h>

// ATTENTION : chaque servomoteur ajoute son propre appel de courant. Une
// alimentation externe 5-6 V est necessaire, masse reliee a celle de la carte,
// dimensionnee a environ 1 A par servomoteur en charge. Alimentes par la
// broche 5V de l'Arduino, ils tremblent des qu'ils forcent et font redemarrer
// la carte.
const byte MOVEMENT_COUNT      = 2;
const byte SERVOS_PER_MOVEMENT = 2;

// Un mouvement par ligne : la premiere ligne est le mouvement 1. Doit
// correspondre a BuildChannels() dans MainWindow.xaml.cs.
//
//   mouvement 1, broches  9 et 10 : LEVAGE      - montee et descente de la charge
//   mouvement 2, broches 11 et 12 : ORIENTATION - pivotement de la fleche
const byte MOVEMENT_PINS[MOVEMENT_COUNT][SERVOS_PER_MOVEMENT] = {
  {  9, 10 },
  { 11, 12 },
};

// Sens de montage de chaque paire. true = servomoteurs montes face a face,
// donc commandes en sens contraire : le second recoit l'angle en miroir du
// premier. Passer a false une paire montee dans le meme sens, auquel cas ses
// deux servomoteurs recevront la meme consigne.
const bool MOVEMENT_MIRRORED[MOVEMENT_COUNT] = { true, true };

// Debattement reellement exploitable. En dessous de 15 et au dessus de 165 la
// plupart des servomoteurs arrivent en butee mecanique : ils forcent, chauffent
// et consomment beaucoup. Ces bornes doivent rester identiques a celles des
// curseurs de l'IHM, sinon l'ecran affiche un angle jamais atteint.
const int ANGLE_MIN  = 15;
const int ANGLE_MAX  = 165;
const int ANGLE_INIT = 90;

const byte BUFFER_SIZE = 12; // "S12:165" + marge ; au dela la ligne est invalide

// VITESSE MAXIMALE DES MOUVEMENTS.
//
// Servo::write() envoie le palonnier a sa vitesse maximale, ce qui secoue la
// structure de la grue et les pieces PLA a chaque changement de consigne. On
// ne va donc pas directement a la consigne : on s'en approche de
// RAMP_STEP_DEGREES degres toutes les RAMP_INTERVAL_MS millisecondes.
//
// Avec 1 degre toutes les 30 ms : environ 33 degres par seconde, soit la
// course complete, 15 a 165 degres, en 4,5 secondes. Augmenter l'intervalle
// pour ralentir, le diminuer pour accelerer.
//
// C'est une LIMITE de vitesse, pas une duree fixe : un deplacement lent du
// curseur, qui avance de moins d'un degre par intervalle, passe sans etre
// freine. Seuls les sauts, typiquement les boutons de rappel, sont lisses.
const byte         RAMP_STEP_DEGREES = 1;
const unsigned int RAMP_INTERVAL_MS  = 30;

Servo servos[MOVEMENT_COUNT][SERVOS_PER_MOVEMENT];

int targetAngle[MOVEMENT_COUNT];   // consigne demandee par l'operateur
int currentAngle[MOVEMENT_COUNT];  // position reellement appliquee, qui la rejoint
unsigned long lastRampMs = 0;

char buffer[BUFFER_SIZE];
byte length     = 0;
bool discarding = false; // true = ligne trop longue, on jette jusqu'au '\n'

// Angle symetrique dans le debattement : 15 <-> 165, et 90 reste 90.
int mirrorAngle(int angle) {
  return ANGLE_MIN + ANGLE_MAX - angle;
}

// Ecrit une consigne sur tous les servomoteurs d'un mouvement, dans la meme
// instruction pour qu'ils restent synchrones. Quand la paire est montee face a
// face, les servomoteurs de rang impair recoivent l'angle en miroir.
void applyMovement(byte index, int angle) {
  for (byte j = 0; j < SERVOS_PER_MOVEMENT; j++) {
    bool invert = MOVEMENT_MIRRORED[index] && (j % 2 == 1);
    servos[index][j].write(invert ? mirrorAngle(angle) : angle);
  }
}

void setup() {
  Serial.begin(9600);

  for (byte i = 0; i < MOVEMENT_COUNT; i++) {
    for (byte j = 0; j < SERVOS_PER_MOVEMENT; j++) {
      servos[i][j].attach(MOVEMENT_PINS[i][j]);
    }
    targetAngle[i]  = ANGLE_INIT;
    currentAngle[i] = ANGLE_INIT;
    applyMovement(i, ANGLE_INIT);
  }

  // La ligne serie n'est pas stable pendant le redemarrage de la carte, et
  // surtout dans les secondes qui suivent un televersement : le recepteur
  // interprete alors du bruit comme des caracteres. Observe en essai, cela
  // suffisait a composer un nombre valide et donc a deplacer les servomoteurs
  // tout seuls au demarrage. On jette ce qui est arrive avant d'ecouter.
  delay(100);
  while (Serial.available() > 0) {
    Serial.read();
  }

  Serial.print("READY ");
  Serial.print(MOVEMENT_COUNT);
  Serial.print(' ');
  Serial.println(MOVEMENT_COUNT * SERVOS_PER_MOVEMENT);
}

void loop() {
  // Avancee des mouvements vers leur consigne, a cadence fixe.
  unsigned long now = millis();
  if (now - lastRampMs >= RAMP_INTERVAL_MS) {
    lastRampMs = now;
    stepMovements();
  }

  // Lecture caractere par caractere : contrairement a Serial.readStringUntil(),
  // cette boucle ne bloque jamais une seconde sur une fin de ligne qui n'arrive
  // pas, et n'alloue pas d'objet String a chaque commande.
  while (Serial.available() > 0) {
    char c = Serial.read();

    if (c == '\n' || c == '\r') {
      if (!discarding && length > 0) {
        buffer[length] = '\0';
        handleCommand(buffer);
      }
      length     = 0;
      discarding = false;
    }
    else if (discarding) {
      // on ignore le reste de la ligne trop longue
    }
    else if (length < BUFFER_SIZE - 1) {
      buffer[length++] = c;
    }
    else {
      discarding = true;
      length     = 0;
    }
  }
}

// Rapproche chaque mouvement de sa consigne d'un pas au plus. Signale par un
// DONE le moment ou un mouvement atteint sa consigne : sur une grue, savoir
// que le deplacement est termine vaut autant que savoir qu'il a ete demande.
void stepMovements() {
  for (byte i = 0; i < MOVEMENT_COUNT; i++) {
    int gap = targetAngle[i] - currentAngle[i];
    if (gap == 0) {
      continue;
    }

    int step = (int)RAMP_STEP_DEGREES;
    if (step > abs(gap)) {
      step = abs(gap);     // dernier pas : on ne depasse jamais la consigne
    }

    currentAngle[i] += (gap > 0) ? step : -step;
    applyMovement(i, currentAngle[i]);

    if (currentAngle[i] == targetAngle[i]) {
      Serial.print("DONE ");
      Serial.print(i + 1);
      Serial.print(' ');
      Serial.println(currentAngle[i]);
    }
  }
}

// Renvoie true si la chaine est non vide et composee uniquement de chiffres.
bool isNumber(const char* s) {
  if (*s == '\0') {
    return false;
  }
  for (const char* p = s; *p != '\0'; p++) {
    if (*p < '0' || *p > '9') {
      return false;
    }
  }
  return true;
}

void rejectLine(const char* line) {
  Serial.print("ERR ");
  Serial.println(line);
}

// Applique une ligne complete recue du PC.
void handleCommand(char* line) {
  byte  index  = 0;      // mouvement vise, 0 par defaut (compatibilite mono-voie)
  char* digits = line;   // partie "angle" de la ligne

  if (line[0] == 'S' || line[0] == 's') {
    char* colon = strchr(line, ':');
    if (colon == NULL) {
      rejectLine(line);
      return;
    }

    // On coupe temporairement la chaine sur le ':' pour valider le numero de
    // mouvement seul, puis on la restaure afin que le message ERR reste lisible.
    *colon = '\0';
    bool validMovement = isNumber(line + 1);
    int  movement      = validMovement ? atoi(line + 1) : 0;
    *colon = ':';

    if (!validMovement || movement < 1 || movement > MOVEMENT_COUNT) {
      rejectLine(line);
      return;
    }

    index  = movement - 1;
    digits = colon + 1;
  }

  // Une ligne vide ou du bruit de ligne donnait 0 avec String::toInt(), donc un
  // constrain() a ANGLE_MIN : les servomoteurs claquaient en butee tout seuls.
  if (!isNumber(digits)) {
    rejectLine(line);
    return;
  }

  int angle = constrain(atoi(digits), ANGLE_MIN, ANGLE_MAX);

  if (angle == targetAngle[index]) {
    return; // consigne inchangee : rien a faire, et surtout rien a emettre
  }

  // On pose la consigne ; c'est stepMovements() qui l'atteindra, a la vitesse
  // autorisee. L'accuse porte la consigne demandee, pas la position courante.
  targetAngle[index] = angle;

  Serial.print("OK ");
  Serial.print(index + 1);
  Serial.print(' ');
  Serial.println(angle);
}
