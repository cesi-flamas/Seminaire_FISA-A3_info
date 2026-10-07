/*
 * Pilotage des mouvements de la grue depuis l'IHM WPF
 * "Seminaire_FISA-A3_servomotor-controller".
 *
 * Chaque MOUVEMENT de la grue est entraine par DEUX servomoteurs montes de
 * part et d'autre du mecanisme. Montes face a face, ils doivent tourner en
 * sens contraire pour entrainer la charge dans le meme sens : le second recoit
 * donc l'angle en miroir du premier. Leur donner la meme consigne les ferait
 * se combattre, forcer et chauffer jusqu'a la casse.
 *
 * Le miroir est applique ICI et non cote PC a dessein. L'IHM n'emet qu'une
 * commande toutes les 50 ms, en tourniquet sur les mouvements : si elle devait
 * piloter les deux servomoteurs d'une paire separement, les deux moities
 * bougeraient a 50 ms d'intervalle et se combattraient a chaque deplacement.
 * Traitees dans la meme instruction, elles restent exactement synchrones.
 *
 * Protocole serie (9600 bauds, 8N1) :
 *   PC -> carte : "S<mouvement>:<angle>\n", par exemple "S2:120\n"
 *                 "<angle>\n" seul est accepte et s'applique au mouvement 1
 *   carte -> PC : "READY <mouvements> <servomoteurs>"  au demarrage
 *                 "OK <mouvement> <angle>"  quand une consigne a change
 *                 "ERR <ligne>"             quand la ligne recue est invalide
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

// ATTENTION : chaque servomoteur ajoute son propre appel de courant, et il y
// en a desormais quatre. Une alimentation externe 5-6 V d'au moins 3 A est
// necessaire, masse reliee a celle de la carte. Alimentes par la broche 5V de
// l'Arduino, ils tremblent des qu'ils forcent et font redemarrer la carte.
const byte MOVEMENT_COUNT      = 2;
const byte SERVOS_PER_MOVEMENT = 2;

// Un mouvement par ligne, dans l'ordre des voies : la premiere ligne est le
// mouvement 1. Doit correspondre a BuildChannels() dans MainWindow.xaml.cs.
//
//   mouvement 1, broches  9 et 10 : ORIENTATION - pivotement de la fleche
//   mouvement 2, broches 11 et  3 : LEVAGE      - montee et descente de la charge
const byte MOVEMENT_PINS[MOVEMENT_COUNT][SERVOS_PER_MOVEMENT] = {
  {  9, 10 },
  { 11,  3 },
};

// Passer a false si les deux servomoteurs d'un mouvement sont finalement
// montes dans le meme sens : ils recevront alors la meme consigne.
const bool MOVEMENT_MIRRORED[MOVEMENT_COUNT] = { true, true };

// Debattement reellement exploitable. En dessous de 15 et au dessus de 165 la
// plupart des servomoteurs arrivent en butee mecanique : ils forcent, chauffent
// et consomment beaucoup. Ces bornes doivent rester identiques a celles des
// curseurs de l'IHM, sinon l'ecran affiche un angle jamais atteint.
const int ANGLE_MIN  = 15;
const int ANGLE_MAX  = 165;
const int ANGLE_INIT = 90;

const byte BUFFER_SIZE = 12; // "S12:165" + marge ; au dela la ligne est invalide

Servo servos[MOVEMENT_COUNT][SERVOS_PER_MOVEMENT];
int   currentAngle[MOVEMENT_COUNT];

char buffer[BUFFER_SIZE];
byte length     = 0;
bool discarding = false; // true = ligne trop longue, on jette jusqu'au '\n'

// Angle symetrique dans le debattement : 15 <-> 165, et 90 reste 90.
int mirrorAngle(int angle) {
  return ANGLE_MIN + ANGLE_MAX - angle;
}

// Ecrit une consigne sur les deux servomoteurs d'un mouvement, en appliquant
// le miroir au second si le montage l'impose.
void applyMovement(byte index, int angle) {
  servos[index][0].write(angle);
  servos[index][1].write(MOVEMENT_MIRRORED[index] ? mirrorAngle(angle) : angle);
}

void setup() {
  Serial.begin(9600);

  for (byte i = 0; i < MOVEMENT_COUNT; i++) {
    for (byte j = 0; j < SERVOS_PER_MOVEMENT; j++) {
      servos[i][j].attach(MOVEMENT_PINS[i][j]);
    }
    currentAngle[i] = ANGLE_INIT;
    applyMovement(i, ANGLE_INIT);
  }

  Serial.print("READY ");
  Serial.print(MOVEMENT_COUNT);
  Serial.print(' ');
  Serial.println(MOVEMENT_COUNT * SERVOS_PER_MOVEMENT);
}

void loop() {
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

  if (angle == currentAngle[index]) {
    return; // consigne inchangee : rien a faire, et surtout rien a emettre
  }

  currentAngle[index] = angle;
  applyMovement(index, angle);

  Serial.print("OK ");
  Serial.print(index + 1);
  Serial.print(' ');
  Serial.println(angle);
}
