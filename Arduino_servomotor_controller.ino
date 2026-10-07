/*
 * Pilotage de plusieurs servomoteurs depuis l'IHM WPF
 * "Seminaire_FISA-A3_servomotor-controller".
 *
 * Un servomoteur par seringue : adapter SERVO_COUNT et SERVO_PINS au nombre
 * de verins de la grue. La bibliotheque Servo en gere jusqu'a douze sur une
 * Uno, mais c'est l'alimentation qui limite en pratique bien avant.
 *
 * Protocole serie (9600 bauds, 8N1) :
 *   PC -> carte : "S<voie>:<angle>\n", par exemple "S2:120\n" (voies de 1 a N)
 *                 "<angle>\n" seul est accepte et s'applique a la voie 1
 *   carte -> PC : "READY <N>"      au demarrage, N = nombre de voies
 *                 "OK <voie> <angle>" quand une consigne a change
 *                 "ERR <ligne>"    quand la ligne recue est invalide
 *
 * La carte ne repond QUE sur changement de consigne : l'IHM emet en continu
 * pendant que l'on deplace un curseur, et un accuse par message saturait le
 * tampon d'emission de 64 octets, ce qui bloquait Serial.print() et faisait
 * decrocher les servomoteurs de plusieurs secondes.
 */

#include <Servo.h>

// ATTENTION : chaque servomoteur ajoute son propre appel de courant. Deux
// microservos en charge demandent deja plus que ce qu'une carte Arduino peut
// fournir : l'alimentation externe 5-6 V, 2 A au minimum et davantage si l'on
// ajoute des voies, n'est pas optionnelle, et sa masse doit etre reliee a
// celle de la carte.
// Affectation des voies de la grue. L'ordre fixe le numero de voie : la
// premiere broche du tableau est la voie 1. Il doit correspondre a
// ServoDefinitions dans MainWindow.xaml.cs, qui porte les memes roles.
//
//   voie 1, broche  9 : ORIENTATION - pivotement de la fleche, gauche <-> droite
//   voie 2, broche 10 : LEVAGE      - montee et descente de la charge, haut <-> bas
const byte SERVO_COUNT = 2;
const byte SERVO_PINS[SERVO_COUNT] = { 9, 10 };

// Debattement reellement exploitable. En dessous de 15 et au dessus de 165 la
// plupart des servomoteurs arrivent en butee mecanique : ils forcent, chauffent
// et consomment beaucoup. Ces bornes doivent rester identiques a celles des
// sliders de l'IHM, sinon l'ecran affiche un angle jamais atteint.
const int ANGLE_MIN  = 15;
const int ANGLE_MAX  = 165;
const int ANGLE_INIT = 90;

const byte BUFFER_SIZE = 12; // "S12:165" + marge ; au dela la ligne est invalide

Servo servos[SERVO_COUNT];
int   currentAngle[SERVO_COUNT];

char buffer[BUFFER_SIZE];
byte length     = 0;
bool discarding = false; // true = ligne trop longue, on jette jusqu'au '\n'

void setup() {
  Serial.begin(9600);

  for (byte i = 0; i < SERVO_COUNT; i++) {
    servos[i].attach(SERVO_PINS[i]);
    currentAngle[i] = ANGLE_INIT;
    servos[i].write(ANGLE_INIT);
  }

  Serial.print("READY ");
  Serial.println(SERVO_COUNT);
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
  byte  index  = 0;      // voie visee, 0 par defaut (compatibilite mono-servo)
  char* digits = line;   // partie "angle" de la ligne

  if (line[0] == 'S' || line[0] == 's') {
    char* colon = strchr(line, ':');
    if (colon == NULL) {
      rejectLine(line);
      return;
    }

    // On coupe temporairement la chaine sur le ':' pour valider le numero de
    // voie seul, puis on la restaure afin que le message ERR reste lisible.
    *colon = '\0';
    bool validChannel = isNumber(line + 1);
    int  channel      = validChannel ? atoi(line + 1) : 0;
    *colon = ':';

    if (!validChannel || channel < 1 || channel > SERVO_COUNT) {
      rejectLine(line);
      return;
    }

    index  = channel - 1;
    digits = colon + 1;
  }

  // Une ligne vide ou du bruit de ligne donnait 0 avec String::toInt(), donc un
  // constrain() a ANGLE_MIN : le servomoteur claquait en butee tout seul.
  if (!isNumber(digits)) {
    rejectLine(line);
    return;
  }

  int angle = constrain(atoi(digits), ANGLE_MIN, ANGLE_MAX);

  if (angle == currentAngle[index]) {
    return; // consigne inchangee : rien a faire, et surtout rien a emettre
  }

  currentAngle[index] = angle;
  servos[index].write(angle);

  Serial.print("OK ");
  Serial.print(index + 1);
  Serial.print(' ');
  Serial.println(angle);
}
