# SmartAccess

Sistem IoT de control al accesului bazat pe amprentă.

- `ESP8266/` – firmware senzor amprentă + servo + buzzer
- `ESP32-CAM/` – firmware flux video
- `SmartAccess/SmartAccess_Web/` – backend Node.js + interfață web (PostgreSQL)
- `SmartAccess/SmartAccess_Mobile/` – aplicație .NET MAUI

## Configurare

- **Web:** `cd SmartAccess/SmartAccess_Web && cp .env.example .env` (completează datele), apoi `npm install` și `npm start`. Schema bazei de date: `server/schema.sql`.
- **Firmware:** în `ESP8266/` și `ESP32-CAM/` copiază `secrets.example.h` ca `secrets.h` și completează datele WiFi.
- **Mobile:** deschide `SmartAccess/SmartAccess.sln` în Visual Studio.
