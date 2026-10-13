# SmartAccess

IoT fingerprint-based access control system.

- `ESP8266/` – firmware for the fingerprint sensor, servo lock and buzzer
- `ESP32-CAM/` – firmware for the live video stream
- `SmartAccess/SmartAccess_Web/` – Node.js backend + web interface (PostgreSQL)
- `SmartAccess/SmartAccess_Mobile/` – .NET MAUI mobile app

## Setup

- **Web:** `cd SmartAccess/SmartAccess_Web && cp .env.example .env` (fill in your values), then `npm install`, `npm run db:init` and `npm start`.
- **Firmware:** in `ESP8266/` and `ESP32-CAM/`, copy `secrets.example.h` to `secrets.h` and fill in your WiFi credentials.
- **Mobile:** open `SmartAccess/SmartAccess.sln` in Visual Studio.

Default admin account (created by `seed.sql`): `admin@smartaccess.local` / `admin123`. Change the password after the first login.
