#include <Servo.h>

Servo myServo;  // Create servo object
const int servoPin = 9; // Servo connected to pin 93

void setup() {
  Serial.begin(9600); // Start serial communication
  myServo.attach(servoPin); // Attach servo to pin 9
}

void loop() {
  if (Serial.available() > 0) {
    String angleStr = Serial.readStringUntil('\n'); // Read angle as string
    if(angleStr.startsWith("angle")){
      Serial.println(myServo.read());
    }
    else {
    int angle = angleStr.toInt(); // Convert to integer
    // Constrain angle to valid servo range (0-180)
    angle = constrain(angle, 0, 180);
    myServo.write(angle); // Move servo to the angle
    Serial.println(angle);
    }
    
  }
}