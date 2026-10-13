#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <Adafruit_Fingerprint.h>
#include <SoftwareSerial.h>
#include <Servo.h>
#include <EEPROM.h>

// Configurare WiFi
#include "secrets.h"
const char* WIFI_SSID = SECRET_WIFI_SSID;
const char* WIFI_PASS = SECRET_WIFI_PASS;

// Pini
#define SERVO_PIN   D1
#define BUZZER_PIN  D0

// Senzor Amprentă pe D5 (RX soft) și D6 (TX soft)
SoftwareSerial mySerial(D5, D6);
Adafruit_Fingerprint finger = Adafruit_Fingerprint(&mySerial);

Servo usaServo;
ESP8266WebServer server(80);

// Tastatura 4x3
// Randuri: D4, RX, TX, D8
// Coloane: D2, D3, D7
const byte KP_ROWS = 4;
const byte KP_COLS = 3;
char keys[KP_ROWS][KP_COLS] = {
  {'1','2','3'},
  {'4','5','6'},
  {'7','8','9'},
  {'*','0','#'}
};
byte rowPins[KP_ROWS] = {D4, 3, 1, D8};   // R1=D4, R2=RX, R3=TX, R4=D8
byte colPins[KP_COLS] = {D2, D3, D7};     // C1=D2, C2=D3, C3=D7

#define KP_DEBOUNCE 200
unsigned long lastScanTime = 0;
char lastKeyScanned = 0;

// Scanare manuala: randul curent OUTPUT LOW, restul randurilor INPUT_PULLUP,
// coloanele mereu INPUT_PULLUP. Inlocuieste librăria Keypad.
char scanKeypad() {
  char foundKey = 0;

  for (byte r = 0; r < KP_ROWS; r++) {
    // Randuri idle = OUTPUT HIGH (nu INPUT_PULLUP) ca D8 sa nu traga coloanele in jos
    for (byte i = 0; i < KP_ROWS; i++) { pinMode(rowPins[i], OUTPUT); digitalWrite(rowPins[i], HIGH); }
    for (byte c = 0; c < KP_COLS; c++) pinMode(colPins[c], INPUT_PULLUP);

    digitalWrite(rowPins[r], LOW);
    delayMicroseconds(50);

    for (byte c = 0; c < KP_COLS; c++) {
      if (digitalRead(colPins[c]) == LOW) {
        foundKey = keys[r][c];
        break;
      }
    }
    if (foundKey) break;
  }

  for (byte i = 0; i < KP_ROWS; i++) { pinMode(rowPins[i], OUTPUT); digitalWrite(rowPins[i], HIGH); }

  if (foundKey) {
    if (foundKey == lastKeyScanned && millis() - lastScanTime < KP_DEBOUNCE) {
      return 0;
    }
    lastKeyScanned = foundKey;
    lastScanTime = millis();
    return foundKey;
  }

  lastKeyScanned = 0;
  return 0;
}

// PIN acces
#define PIN_MAX_LEN   8
String correctPin   = "1234";
String pinBuffer    = "";
unsigned long lastKeyTime = 0;
#define PIN_TIMEOUT  10000

void loadPin() {
  EEPROM.begin(16);
  if (EEPROM.read(0) == 0xAB) {
    char buf[PIN_MAX_LEN + 1] = {0};
    for (int i = 0; i < PIN_MAX_LEN; i++) buf[i] = EEPROM.read(1 + i);
    buf[PIN_MAX_LEN] = '\0';
    String loaded = String(buf);
    bool valid = (loaded.length() >= 4);
    for (unsigned int i = 0; i < loaded.length() && valid; i++) {
      if (!isDigit(loaded[i])) valid = false;
    }
    if (valid) correctPin = loaded;
  }
  EEPROM.end();
}

void savePin(String pin) {
  EEPROM.begin(16);
  EEPROM.write(0, 0xAB);
  for (int i = 0; i < PIN_MAX_LEN; i++) {
    EEPROM.write(1 + i, i < (int)pin.length() ? pin[i] : 0);
  }
  EEPROM.commit();
  EEPROM.end();
}

// Stare sistem
String statusText = "Idle - Amprenta sau PIN";
String logText    = "";
const int MAX_LOG_LEN = 2000;

#define MAX_MEMBERS 10
String memberNames[MAX_MEMBERS];

bool enrollMode = false;
int  enrollSlot = -1;
String enrollName = "";
unsigned long enrollTimeout = 0;
int enrollStep = 0;

bool unlockRequested = false;

bool unlockActive = false;
unsigned long unlockStartTime = 0;
#define UNLOCK_DURATION 4000

bool detachPending = false;
unsigned long detachTime = 0;
#define DETACH_DELAY  180
#define SERVO_CLOSED   20

bool deniedCooldown = false;
unsigned long deniedCooldownStart = 0;
#define DENIED_COOLDOWN 2000

bool enrollDone = false;
unsigned long enrollDoneTime = 0;
#define ENROLL_DONE_DELAY 3000

// Functii utilitare

void addLog(String line) {
  logText += line + "\n";
  if (logText.length() > MAX_LOG_LEN) {
    int idx = logText.indexOf('\n', logText.length() - MAX_LOG_LEN);
    if (idx > 0) logText = logText.substring(idx + 1);
  }
}

void setStatus(String s) {
  statusText = s;
}

void bipScurt(int count) {
  for (int i = 0; i < count; i++) {
    digitalWrite(BUZZER_PIN, LOW);
    delay(100);
    digitalWrite(BUZZER_PIN, HIGH);
    if (i < count - 1) delay(100);
  }
}

void bipLung() {
  digitalWrite(BUZZER_PIN, LOW);
  delay(800);
  digitalWrite(BUZZER_PIN, HIGH);
}

void bipTasta() {
  digitalWrite(BUZZER_PIN, LOW);
  delay(30);
  digitalWrite(BUZZER_PIN, HIGH);
}

void startUnlock() {
  if (unlockActive || detachPending) return;
  usaServo.attach(SERVO_PIN);
  usaServo.write(90);
  bipScurt(2);
  unlockActive    = true;
  unlockStartTime = millis();
  addLog("Usa deschisa!");
}

String jsonEscape(String s) {
  s.replace("\\", "\\\\");
  s.replace("\"", "\\\"");
  s.replace("\n", "\\n");
  s.replace("\r", "");
  return s;
}

// Handlere HTTP

void handleData() {
  String json = "{\"status\":\"" + jsonEscape(statusText) +
                "\",\"log\":\"" + jsonEscape(logText) + "\"}";
  server.sendHeader("Access-Control-Allow-Origin", "*");
  server.send(200, "application/json", json);
}

void handleEnroll() {
  String name    = server.arg("name");
  String slotStr = server.arg("slot");

  if (name.length() == 0 || slotStr.length() == 0) {
    server.send(400, "text/plain", "Lipsesc parametri: name, slot");
    return;
  }

  int slot = slotStr.toInt();
  if (slot < 0 || slot >= MAX_MEMBERS) {
    server.send(400, "text/plain", "Slot invalid (0-9)");
    return;
  }

  memberNames[slot] = name;
  enrollMode    = true;
  enrollSlot    = slot;
  enrollName    = name;
  enrollStep    = 0;
  enrollTimeout = millis() + 60000;

  setStatus("Pune degetul pe senzor");
  addLog("--- Inrolare " + name + " (slot " + String(slot) + ") inceputa ---");

  server.sendHeader("Access-Control-Allow-Origin", "*");
  server.send(200, "text/plain", "Inrolare pornita pentru " + name);
}

void handleDelete() {
  String slotStr = server.arg("slot");

  if (slotStr.length() == 0) {
    server.send(400, "text/plain", "Lipseste parametrul: slot");
    return;
  }

  int slot = slotStr.toInt();
  if (slot < 0 || slot >= MAX_MEMBERS) {
    server.send(400, "text/plain", "Slot invalid (0-9)");
    return;
  }

  uint8_t p  = finger.deleteModel(slot);
  String name = memberNames[slot];

  if (p == FINGERPRINT_OK) {
    addLog("Amprenta stearsa: " + name + " (slot " + String(slot) + ")");
    setStatus("Amprenta " + name + " stearsa cu succes");
  } else {
    addLog("Stergere slot " + String(slot) + " (poate era deja gol)");
    setStatus("Slot " + String(slot) + " eliberat");
  }

  memberNames[slot] = "";
  server.sendHeader("Access-Control-Allow-Origin", "*");
  server.send(200, "text/plain", "Slot " + String(slot) + " sters");
}

void handleUnlock() {
  addLog("Deblocare din aplicatie!");
  setStatus("Usa deschisa din aplicatie");
  unlockRequested = true;
  server.sendHeader("Access-Control-Allow-Origin", "*");
  server.send(200, "text/plain", "Usa se deschide");
}

void handleSetPin() {
  String newPin = server.arg("pin");
  if (newPin.length() < 4 || newPin.length() > PIN_MAX_LEN) {
    server.send(400, "text/plain", "PIN trebuie sa fie 4-8 cifre");
    return;
  }
  for (unsigned int i = 0; i < newPin.length(); i++) {
    if (!isDigit(newPin[i])) {
      server.send(400, "text/plain", "PIN trebuie sa contina doar cifre");
      return;
    }
  }
  correctPin = newPin;
  savePin(newPin);
  addLog("PIN actualizat din aplicatie");
  server.sendHeader("Access-Control-Allow-Origin", "*");
  server.send(200, "text/plain", "PIN actualizat");
}

void handleNotFound() {
  server.sendHeader("Access-Control-Allow-Origin", "*");
  server.send(404, "text/plain", "Endpoint necunoscut");
}

// Inrolare amprenta (masina de stari, pas cu pas in loop)

void processEnrollment() {
  if (!enrollMode) return;

  if (millis() > enrollTimeout) {
    addLog("Timeout inrolare - 60 secunde expirate!");
    setStatus("Eroare: Timeout inrolare");
    enrollMode = false;
    enrollDone     = true;
    enrollDoneTime = millis();
    return;
  }

  uint8_t p;

  switch (enrollStep) {
    case 0:
      p = finger.getImage();
      if (p == FINGERPRINT_NOFINGER) return;
      if (p != FINGERPRINT_OK) { addLog("Eroare la citirea imaginii. Incearca din nou."); return; }
      addLog("Deget detectat! Procesez imaginea...");
      p = finger.image2Tz(1);
      if (p != FINGERPRINT_OK) { addLog("Eroare la procesarea imaginii. Pune degetul din nou."); return; }
      addLog("Prima imagine OK!");

      // Respinge daca amprenta e deja inrolata pe alt slot
      p = finger.fingerFastSearch();
      if (p == FINGERPRINT_OK) {
        String existingName = memberNames[finger.fingerID];
        addLog("Eroare: Amprenta este deja inregistrata (" + (existingName.length() ? existingName : String("slot ") + finger.fingerID) + ")");
        setStatus("Eroare: Amprenta deja inregistrata pe alt cont");
        enrollMode = false;
        enrollDone     = true;
        enrollDoneTime = millis();
        return;
      }

      setStatus("Ridica degetul de pe senzor");
      addLog("Ridica degetul de pe senzor...");
      enrollStep = 1;
      break;

    case 1:
      p = finger.getImage();
      if (p == FINGERPRINT_NOFINGER) {
        addLog("Deget ridicat. Pune acelasi deget din nou.");
        setStatus("Pune acelasi deget din nou");
        enrollStep = 2;
      }
      break;

    case 2:
      p = finger.getImage();
      if (p == FINGERPRINT_NOFINGER) return;
      if (p != FINGERPRINT_OK) { addLog("Eroare la citirea imaginii. Incearca din nou."); return; }
      addLog("A doua imagine detectata! Procesez...");
      p = finger.image2Tz(2);
      if (p != FINGERPRINT_OK) { addLog("Eroare la procesarea imaginii. Pune degetul din nou."); return; }
      enrollStep = 3;
      break;

    case 3:
      addLog("Creez modelul de amprenta...");
      p = finger.createModel();
      if (p != FINGERPRINT_OK) {
        addLog("Eroare: Amprentele nu se potrivesc! Incearca din nou de la inceput.");
        setStatus("Eroare: Amprentele nu se potrivesc");
        enrollMode = false;
        enrollDone     = true;
        enrollDoneTime = millis();
        return;
      }
      addLog("Model creat cu succes! Salvez pe slot " + String(enrollSlot) + "...");
      p = finger.storeModel(enrollSlot);
      if (p != FINGERPRINT_OK) {
        addLog("Eroare la salvarea modelului pe senzor!");
        setStatus("Eroare: Nu am putut salva amprenta");
        enrollMode = false;
        enrollDone     = true;
        enrollDoneTime = millis();
        return;
      }
      addLog("=== Amprenta " + enrollName + " salvata cu SUCCES pe slot " + String(enrollSlot) + " ===");
      setStatus("Amprenta salvata cu succes");
      bipScurt(3);
      enrollMode = false;
      enrollDone     = true;
      enrollDoneTime = millis();
      break;
  }
}

int verificaAmprenta() {
  uint8_t p = finger.getImage();
  if (p != FINGERPRINT_OK) return -1;
  p = finger.image2Tz();
  if (p != FINGERPRINT_OK) return -1;
  p = finger.fingerFastSearch();
  if (p != FINGERPRINT_OK) return -2;
  return finger.fingerID;
}

// Setup

void setup() {
  // Pinii TX si RX raman in modul UART dupa boot chiar fara Serial.begin().
  // Serial.end() le elibereaza la nivel hardware, apoi reaplica INPUT_PULLUP pt. tastatura.
  Serial.begin(115200);
  delay(10);
  Serial.end();
  pinMode(D4, INPUT_PULLUP);
  pinMode(3,  INPUT_PULLUP);
  pinMode(1,  INPUT_PULLUP);
  pinMode(D8, INPUT_PULLUP);
  pinMode(D2, INPUT_PULLUP);
  pinMode(D3, INPUT_PULLUP);
  pinMode(D7, INPUT_PULLUP);

  pinMode(BUZZER_PIN, OUTPUT);
  digitalWrite(BUZZER_PIN, HIGH);

  usaServo.attach(SERVO_PIN);
  usaServo.write(SERVO_CLOSED);
  delay(500);
  usaServo.detach();

  loadPin();

  finger.begin(57600);
  if (!finger.verifyPassword()) {
    // 5 bipuri lungi = senzor negasit
    for (int i = 0; i < 5; i++) {
      digitalWrite(BUZZER_PIN, LOW); delay(300);
      digitalWrite(BUZZER_PIN, HIGH); delay(200);
    }
    while (1) { delay(1); }
  }
  bipScurt(1);

  for (int i = 0; i < MAX_MEMBERS; i++) memberNames[i] = "";

  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASS);

  int attempts = 0;
  while (WiFi.status() != WL_CONNECTED && attempts < 40) {
    delay(500);
    attempts++;
  }

  if (WiFi.status() == WL_CONNECTED) {
    addLog("WiFi conectat. IP: " + WiFi.localIP().toString());
    bipScurt(2);
  } else {
    addLog("WiFi ESUATA - doar mod local");
    bipLung();
  }

  server.on("/data",    handleData);
  server.on("/enroll",  handleEnroll);
  server.on("/delete",  handleDelete);
  server.on("/unlock",  handleUnlock);
  server.on("/setpin",  handleSetPin);
  server.onNotFound(handleNotFound);
  server.begin();

  setStatus("Idle - Amprenta sau PIN");
  addLog("Sistem pornit! PIN curent: " + correctPin);
}

// Loop principal

void loop() {
  server.handleClient();

  // Tastatura
  char key = scanKeypad();
  if (key) {
    lastKeyTime = millis();

    if (key == '*') {
      // Sterge buffer
      if (pinBuffer.length() > 0) {
        pinBuffer = "";
        bipTasta();
        if (!unlockActive && !enrollMode && !deniedCooldown)
          statusText = "Idle - Amprenta sau PIN";
      }

    } else if (key == '#') {
      // Confirmare PIN
      if (!enrollMode && !unlockActive && !detachPending && !deniedCooldown) {
        if (pinBuffer == correctPin) {
          addLog("Acces PERMIS: PIN corect");
          setStatus("Acces permis: PIN");
          startUnlock();
        } else if (pinBuffer.length() > 0) {
          addLog("Acces RESPINS: PIN incorect!");
          setStatus("Acces respins - PIN gresit");
          bipLung();
          deniedCooldown      = true;
          deniedCooldownStart = millis();
        }
      }
      pinBuffer = "";

    } else {
      // Cifra
      bipTasta();
      if (!enrollMode && pinBuffer.length() < PIN_MAX_LEN) {
        pinBuffer += key;
        String stars = "";
        for (unsigned int i = 0; i < pinBuffer.length(); i++) stars += '*';
        statusText = "PIN: " + stars;
      }
    }
  }

  // Timeout inactivitate tastatura
  if (pinBuffer.length() > 0 && millis() - lastKeyTime > PIN_TIMEOUT) {
    pinBuffer = "";
    if (!unlockActive && !enrollMode && !deniedCooldown)
      statusText = "Idle - Amprenta sau PIN";
  }

  // Unlock din aplicatie
  if (unlockRequested) {
    unlockRequested = false;
    if (!unlockActive) {
      startUnlock();
      setStatus("Usa deschisa din aplicatie");
    }
  }

  // Timer inchidere usa
  if (unlockActive && millis() - unlockStartTime >= UNLOCK_DURATION) {
    usaServo.write(SERVO_CLOSED);
    unlockActive  = false;
    detachPending = true;
    detachTime    = millis();
  }

  if (detachPending && millis() - detachTime >= DETACH_DELAY) {
    usaServo.detach();
    detachPending = false;
    addLog("Usa blocata la loc.");
    setStatus("Idle - Amprenta sau PIN");
  }

  if (enrollDone && millis() - enrollDoneTime >= ENROLL_DONE_DELAY) {
    enrollDone = false;
    setStatus("Idle - Amprenta sau PIN");
  }

  if (deniedCooldown && millis() - deniedCooldownStart >= DENIED_COOLDOWN) {
    deniedCooldown = false;
    setStatus("Idle - Amprenta sau PIN");
  }

  // Modul inrolare
  if (enrollMode) {
    processEnrollment();
    delay(50);
    return;
  }

  // Verificare amprenta (enrollDone blocheaza citirea 3s dupa inrolare)
  if (!unlockActive && !detachPending && !deniedCooldown && !enrollDone) {
    int rezultat = verificaAmprenta();
    if (rezultat >= 0) {
      String name = (rezultat < MAX_MEMBERS && memberNames[rezultat].length() > 0)
                    ? memberNames[rezultat]
                    : "ID " + String(rezultat);
      addLog("Acces PERMIS: " + name + " (slot " + String(rezultat) + ")");
      setStatus("Acces permis: " + name);
      startUnlock();
    } else if (rezultat == -2) {
      addLog("Acces RESPINS: Amprenta necunoscuta!");
      setStatus("Acces respins - amprenta necunoscuta");
      bipLung();
      deniedCooldown      = true;
      deniedCooldownStart = millis();
    }
  }

  delay(50);
}
