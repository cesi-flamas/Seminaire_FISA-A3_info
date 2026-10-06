#include <Servo.h>

Servo myServo;  // Create servo object
const int servoPin = 9; // Servo connected to pin 9

void setup() {
  Serial.begin(9600); // Start serial communication
  myServo.attach(servoPin); // Attach servo to pin 9
}

void loop() {
  if (Serial.available() > 0) {
    String angleStr = Serial.readStringUntil('\n'); // Read angle as string
    int angle = angleStr.toInt(); // Convert to integer

    // Constrain angle to valid servo range (0-180)
    // To prevent servomotor damages, the usable range is limited between 15 and 165 degrees.
    angle = constrain(angle, 15, 165);
    myServo.write(angle); // Move servo to the angle
    Serial.print("Servo moved to: ");
    Serial.println(angle);
  }
}
