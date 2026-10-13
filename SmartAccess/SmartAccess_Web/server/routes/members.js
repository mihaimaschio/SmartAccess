const express = require('express');
const db = require('../db');
const { requireAdmin } = require('../middleware/auth');
require('dotenv').config();

const router = express.Router();

// Helper: fa request la ESP
async function espRequest(path) {
  const espIp = process.env.ESP_IP || 'http://192.168.0.129';
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 3000);
  
  try {
    const response = await fetch(`${espIp}${path}`, { signal: controller.signal });
    clearTimeout(timeout);
    return await response.text();
  } catch (err) {
    clearTimeout(timeout);
    throw new Error('ESP nu raspunde');
  }
}

// POST /api/members/:id/enroll - inroleaza amprenta unui utilizator
router.post('/:id/enroll', requireAdmin, async (req, res) => {
  try {
    const { id } = req.params;
    
    // Gaseste utilizatorul
    const userResult = await db.query('SELECT * FROM users WHERE id = $1', [id]);
    if (userResult.rows.length === 0) {
      return res.status(404).json({ error: 'Utilizator negasit.' });
    }
    
    const user = userResult.rows[0];
    
    // Gaseste primul slot liber (0-9)
    const slotsResult = await db.query('SELECT fingerprint_slot FROM users WHERE fingerprint_slot IS NOT NULL');
    const takenSlots = new Set(slotsResult.rows.map(r => r.fingerprint_slot));
    
    let freeSlot = user.fingerprint_slot; // pastreaza slotul existent daca are
    if (freeSlot === null) {
      for (let i = 0; i <= 9; i++) {
        if (!takenSlots.has(i)) {
          freeSlot = i;
          break;
        }
      }
    }
    
    if (freeSlot === null) {
      return res.status(400).json({ error: 'Toate cele 10 sloturi de amprenta sunt ocupate.' });
    }
    
    // Trimite comanda catre ESP - slotul se salveaza in DB doar dupa confirmarea
    // reala de succes (vezi /enroll-confirm), nu la pornirea inrolarii.
    await espRequest(`/enroll?name=${encodeURIComponent(user.full_name)}&slot=${freeSlot}&type=finger`);

    res.json({
      message: `Inrolare pornita pentru ${user.full_name}`,
      slot: freeSlot
    });
  } catch (err) {
    console.error('Eroare enroll:', err);
    res.status(500).json({ error: err.message || 'Eroare la inrolare.' });
  }
});

// POST /api/members/:id/enroll-confirm - confirma succesul inrolarii si salveaza slotul (admin only)
router.post('/:id/enroll-confirm', requireAdmin, async (req, res) => {
  try {
    const { id } = req.params;
    const { slot } = req.body;

    if (slot === undefined || slot === null || slot < 0 || slot > 9) {
      return res.status(400).json({ error: 'Slot invalid.' });
    }

    const result = await db.query(
      'UPDATE users SET fingerprint_slot = $1, updated_at = CURRENT_TIMESTAMP WHERE id = $2 RETURNING id, full_name, fingerprint_slot',
      [slot, id]
    );

    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'Utilizator negasit.' });
    }

    res.json({ message: 'Amprenta confirmata.', user: result.rows[0] });
  } catch (err) {
    res.status(500).json({ error: 'Eroare la confirmarea inrolarii.' });
  }
});

// DELETE /api/members/:id/fingerprint - sterge amprenta unui utilizator
router.delete('/:id/fingerprint', requireAdmin, async (req, res) => {
  try {
    const { id } = req.params;
    
    const userResult = await db.query('SELECT * FROM users WHERE id = $1', [id]);
    if (userResult.rows.length === 0) {
      return res.status(404).json({ error: 'Utilizator negasit.' });
    }
    
    const user = userResult.rows[0];
    if (user.fingerprint_slot === null) {
      return res.status(400).json({ error: 'Utilizatorul nu are amprenta inregistrata.' });
    }
    
    // Sterge de pe ESP
    try {
      await espRequest(`/delete?slot=${user.fingerprint_slot}`);
    } catch (e) {
      // ESP poate fi offline, continuam cu stergerea din DB
    }
    
    // Sterge din DB
    await db.query('UPDATE users SET fingerprint_slot = NULL, updated_at = CURRENT_TIMESTAMP WHERE id = $1', [id]);
    
    res.json({ message: `Amprenta lui ${user.full_name} a fost stearsa.` });
  } catch (err) {
    res.status(500).json({ error: 'Eroare la stergere amprenta.' });
  }
});

// GET /api/members/enroll-status - status inrolare (polling)
router.get('/enroll-status', requireAdmin, async (req, res) => {
  try {
    const data = await espRequest('/data');
    const parsed = JSON.parse(data);
    res.json(parsed);
  } catch (err) {
    res.status(503).json({ error: 'ESP nu raspunde.', status: 'offline', log: '' });
  }
});

// GET /api/members - lista membri
router.get('/', async (req, res) => {
  try {
    const result = await db.query(
      'SELECT id, full_name, email, role, fingerprint_slot, is_active, is_home, last_seen_at FROM users ORDER BY full_name'
    );
    res.json({ members: result.rows });
  } catch (err) {
    res.status(500).json({ error: 'Eroare server.' });
  }
});

// PUT /api/members/:id/presence - marcheaza manual acasa/plecat.
// Intrarea seteaza automat "acasa" (prin amprenta), dar iesirea se declara
// manual, deoarece usa nu are senzor de iesire. Un utilizator isi poate seta
// propriul status; un admin poate seta statusul oricui.
router.put('/:id/presence', async (req, res) => {
  try {
    const { id } = req.params;
    const { is_home } = req.body;

    if (typeof is_home !== 'boolean') {
      return res.status(400).json({ error: 'Parametru invalid: is_home.' });
    }

    // Doar propriul cont sau un administrator
    if (parseInt(id) !== req.user.id && req.user.role !== 'admin') {
      return res.status(403).json({ error: 'Acces interzis.' });
    }

    const result = await db.query(
      'UPDATE users SET is_home = $1, last_seen_at = NOW() WHERE id = $2 RETURNING id, full_name, is_home',
      [is_home, id]
    );

    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'Utilizator negasit.' });
    }

    res.json({ member: result.rows[0] });
  } catch (err) {
    res.status(500).json({ error: 'Eroare server.' });
  }
});

module.exports = router;
