/*
 * Pilotage d'un servomoteur depuis l'IHM WPF "Seminaire_FISA-A3_servomotor-controller".
 *
 * Protocole serie (9600 bauds, 8N1) :
 *   PC -> carte : un angle en degres suivi d'un retour a la ligne, ex. "120\n"
 *   carte -> PC : "OK <angle>"  quand la consigne a change et a ete appliquee
 *                 "ERR <recu>"  quand la ligne recue n'est pas un nombre valide
 *                 "READY <angle>" une fois au demarrage
 *
 * La carte ne repond QUE sur changement de consigne : l'IHM envoie une position
 * en continu pendant que l'on deplace le curseur, et un accuse par message
 * saturait le tampon d'emission (64 octets), ce qui bloquait Serial.print()
 * et faisait decrocher le servo de plusieurs secondes.
 */

#include <Servo.h>

const int SERVO_PIN  = 9;   // broche de commande du servo

// Debattement reellement exploitable. En dessous de 15 et au dessus de 165 la
// plupart des servos arrivent en butee mecanique : ils forcent, chauffent et
// consomment beaucoup. Ces bornes doivent rester identiques a celles du slider
// de l'IHM, sinon l'ecran affiche un angle que le servo n'atteint jamais.
const int ANGLE_MIN  = 15;
const int ANGLE_MAX  = 165;
const int ANGLE_INIT = 90;  // position de repos au demarrage

const byte BUFFER_SIZE = 8; // "165" + marge ; une ligne plus longue est invalide

Servo myServo;

char buffer[BUFFER_SIZE];
byte length    = 0;
bool discarding = false;    // true = ligne trop longue, on jette jusqu'au '\n'
int  currentAngle = ANGLE_INIT;

void setup() {
  Serial.begin(9600);
  myServo.attach(SERVO_PIN);
  myServo.write(currentAngle);

  Serial.print("READY ");
  Serial.println(currentAngle);
}

void loop() {
  // Lecture caractere par caractere : contrairement a Serial.readStringUntil(),
  // cette boucle ne bloque jamais 1 seconde en attendant une fin de ligne qui
  // n'arrive pas, et n'alloue pas d'objet String a chaque commande.
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

// Applique une ligne complete recue du PC.
void handleCommand(const char* line) {
  // Une ligne vide ou du bruit de ligne donnait 0 avec String::toInt(), donc
  // un constrain() a ANGLE_MIN : le servo claquait en butee tout seul. On
  // verifie donc que la ligne ne contient que des chiffres avant d'agir.
  for (const char* p = line; *p != '\0'; p++) {
    if (*p < '0' || *p > '9') {
      Serial.print("ERR ");
      Serial.println(line);
      return;
    }
  }

  int angle = constrain(atoi(line), ANGLE_MIN, ANGLE_MAX);

  if (angle == currentAngle) {
    return; // consigne inchangee : rien a faire, et surtout rien a emettre
  }

  currentAngle = angle;
  myServo.write(currentAngle);

  Serial.print("OK ");
  Serial.println(currentAngle);
}
