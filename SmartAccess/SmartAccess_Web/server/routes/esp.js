const express = require("express");
const db = require("../db");
require("dotenv").config();

const router = express.Router();

async function espRequest(path) {
  const espIp = process.env.ESP_IP || "http://192.168.0.129";
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 3000);

  try {
    const response = await fetch(`${espIp}${path}`, {
      signal: controller.signal,
    });
    clearTimeout(timeout);
    return await response.text();
  } catch (err) {
    clearTimeout(timeout);
    throw new Error("ESP nu raspunde");
  }
}

// POST /api/esp/unlock - deschide usa
router.post("/unlock", async (req, res) => {
  try {
    await espRequest("/unlock");

    await db.query(
      "INSERT INTO access_log (user_id, access_type, result, details, user_name) VALUES ($1, $2, $3, $4, $5)",
      [
        req.user.id,
        "app_unlock",
        "granted",
        `Unlocked via app by ${req.user.full_name}`,
        req.user.full_name,
      ],
    );

    await db.query(
      "UPDATE users SET is_home = true, last_seen_at = NOW() WHERE id = $1",
      [req.user.id],
    );

    res.json({ message: "Usa deschisa!" });
  } catch (err) {
    res.status(503).json({ error: "ESP nu raspunde. Verifica conexiunea." });
  }
});

// GET /api/esp/status - verificare conexiune ESP
router.get("/status", async (req, res) => {
  try {
    const data = await espRequest("/data");
    const parsed = JSON.parse(data);
    res.json({ online: true, status: parsed.status });
  } catch (err) {
    res.json({ online: false, status: "ESP offline" });
  }
});

// POST /api/esp/setpin - schimba PIN-ul tastaturii
router.post("/setpin", async (req, res) => {
  const { pin } = req.body;
  if (!pin || !/^\d{4,8}$/.test(pin)) {
    return res
      .status(400)
      .json({ error: "PIN-ul trebuie sa fie intre 4 si 8 cifre." });
  }
  try {
    await espRequest(`/setpin?pin=${pin}`);
    res.json({ message: "PIN actualizat!" });
  } catch (err) {
    res.status(503).json({ error: "ESP nu raspunde." });
  }
});

// GET /api/esp/data - date raw de la ESP
router.get("/data", async (req, res) => {
  try {
    const data = await espRequest("/data");
    const parsed = JSON.parse(data);
    res.json(parsed);
  } catch (err) {
    res
      .status(503)
      .json({ error: "ESP nu raspunde.", status: "offline", log: "" });
  }
});

// Verifica ESP la fiecare 3 secunde si salveaza accesarile in baza de date.

let lastLogSnapshot = "";

async function pollEspAndSaveAccess() {
  try {
    const espIp = process.env.ESP_IP || "http://192.168.0.129";
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 3000);

    const response = await fetch(`${espIp}/data`, {
      signal: controller.signal,
    });
    clearTimeout(timeout);
    const data = JSON.parse(await response.text());

    const log = data.log || "";
    if (log === lastLogSnapshot) return;

    // Procesam doar liniile NOI adaugate de la ultimul poll.
    // Daca logul creste (append), luam doar sufixul nou.
    // Daca logul s-a scurtat sau resetat (reboot ESP / trunchiere), resetam
    // tracking-ul si sarim iteratia curenta ca sa evitam duplicate.
    let newContent = "";
    if (lastLogSnapshot === "") {
      // Prima rulare - nu procesam nimic ca sa evitam re-logarea evenimentelor vechi
    } else if (log.startsWith(lastLogSnapshot)) {
      newContent = log.substring(lastLogSnapshot.length);
    } else {
      // Logul s-a trunchiat sau ESP a restartat - cautam suprapunere
      const tail = lastLogSnapshot.slice(-100);
      const overlapIdx = log.indexOf(tail);
      if (overlapIdx !== -1) {
        newContent = log.substring(overlapIdx + tail.length);
      }
      // daca nu gasim suprapunere, sarim iteratia (evitam duplicate)
    }

    lastLogSnapshot = log;

    if (!newContent.trim()) return;

    const lines = newContent.split("\n").filter((l) => l.trim().length > 0);

    for (const line of lines) {
      const lower = line.toLowerCase();

      if (lower.includes("acces permis:") && lower.includes("pin")) {
        // Acces permis prin cod PIN de la tastatura. PIN-ul e partajat, deci
        // nu poate fi atribuit unui anumit utilizator (user_id/user_name null).
        await db.query(
          "INSERT INTO access_log (user_id, access_type, result, details, user_name) VALUES ($1, $2, $3, $4, $5)",
          [null, "pin", "granted", "Keypad PIN access", null],
        );
        console.log("Acces PERMIS logat: PIN");
      } else if (lower.includes("acces permis:")) {
        const match = line.match(/Acces PERMIS: (.+?) \(slot/i);
        const name = match ? match[1] : "Unknown";
        const slotMatch = line.match(/slot (\d+)/);
        const slot = slotMatch ? parseInt(slotMatch[1]) : null;

        let userId = null;
        let dbName = null;
        if (slot !== null) {
          const userResult = await db.query(
            "SELECT id, full_name FROM users WHERE fingerprint_slot = $1",
            [slot],
          );
          if (userResult.rows.length > 0) {
            userId = userResult.rows[0].id;
            dbName = userResult.rows[0].full_name;
          }
        }

        // Numele din baza de date e sursa de adevar: nodul ESP pierde numele
        // membrilor la repornire si logheaza "ID <slot>", asa ca preferam
        // intotdeauna numele din DB si cadem pe cel din log doar daca slotul
        // nu e gasit.
        const displayName = dbName || name;

        await db.query(
          "INSERT INTO access_log (user_id, access_type, result, details, user_name) VALUES ($1, $2, $3, $4, $5)",
          [
            userId,
            "fingerprint",
            "granted",
            `Fingerprint access: ${displayName}`,
            dbName,
          ],
        );
        if (userId !== null) {
          // Senzorul e doar la exterior - orice acces permis inseamna intrare,
          // niciodata iesire (usa se deschide liber din interior, fara scanare).
          await db.query(
            "UPDATE users SET is_home = true, last_seen_at = NOW() WHERE id = $1",
            [userId],
          );
        }
        console.log(`Acces PERMIS logat: ${displayName}`);
      } else if (lower.includes("acces respins")) {
        const isPin = lower.includes("pin incorect");
        const accessType = isPin ? "pin" : "fingerprint";
        const details = isPin
          ? "Incorrect PIN - access denied"
          : "Unknown fingerprint - access denied";
        const notifMessage = isPin
          ? "Unauthorized access attempt! Incorrect PIN entered."
          : "Unauthorized access attempt! Unknown fingerprint detected.";

        await db.query(
          "INSERT INTO access_log (user_id, access_type, result, details) VALUES ($1, $2, $3, $4)",
          [null, accessType, "denied", details],
        );

        // Alerta de acces neautorizat ajunge la toti userii
        const users = await db.query(
          "SELECT id FROM users WHERE is_active = true",
        );
        for (const u of users.rows) {
          await db.query(
            "INSERT INTO notifications (user_id, type, message) VALUES ($1, $2, $3)",
            [u.id, "access_denied", notifMessage],
          );
        }
        console.log(`Acces RESPINS logat`);
      }
    }
  } catch (err) {
    // ESP offline - e ok, incercam din nou la urmatorul poll
  }
}

// Porneste polling-ul la 3 secunde
let pollInterval = null;
function startPolling() {
  if (pollInterval) return;
  console.log("Interogare ESP pornita (la 3 secunde)");
  pollInterval = setInterval(pollEspAndSaveAccess, 3000);
  // Prima verificare imediat
  setTimeout(pollEspAndSaveAccess, 1000);
}

function stopPolling() {
  if (pollInterval) {
    clearInterval(pollInterval);
    pollInterval = null;
  }
}

// Exportam si functia de start polling
router.startPolling = startPolling;
router.stopPolling = stopPolling;

module.exports = router;
