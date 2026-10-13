const express = require('express');
const bcrypt = require('bcryptjs');
const db = require('../db');
const { requireAdmin } = require('../middleware/auth');
require('dotenv').config();

async function deleteEspFingerprint(slot) {
  const espIp = process.env.ESP_IP || 'http://192.168.0.129';
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 3000);
  try {
    await fetch(`${espIp}/delete?slot=${slot}`, { signal: controller.signal });
    clearTimeout(timeout);
  } catch {
    clearTimeout(timeout);
  }
}

const router = express.Router();

// GET /api/users - lista toti utilizatorii (admin only)
router.get('/', requireAdmin, async (req, res) => {
  try {
    const result = await db.query(
      'SELECT id, email, full_name, role, fingerprint_slot, is_active, is_home, created_at FROM users ORDER BY full_name'
    );
    res.json({ users: result.rows });
  } catch (err) {
    res.status(500).json({ error: 'Eroare server.' });
  }
});

// PUT /api/users/:id - editeaza utilizator (admin only)
router.put('/:id', requireAdmin, async (req, res) => {
  try {
    const { id } = req.params;
    const { full_name, email, role, is_active } = req.body;

    // Nu ne putem dezactiva sau retrograda propriul cont
    if (parseInt(id) === req.user.id) {
      if (is_active === false) {
        return res.status(400).json({ error: 'Nu poti dezactiva propriul cont.' });
      }
      if (role && role !== 'admin') {
        return res.status(400).json({ error: 'Nu poti schimba propriul rol.' });
      }
    }

    const result = await db.query(
      `UPDATE users SET 
        full_name = COALESCE($1, full_name),
        email = COALESCE($2, email),
        role = COALESCE($3, role),
        is_active = COALESCE($4, is_active),
        updated_at = CURRENT_TIMESTAMP
      WHERE id = $5
      RETURNING id, email, full_name, role, fingerprint_slot, is_active`,
      [full_name, email, role, is_active, id]
    );

    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'Utilizator negasit.' });
    }

    res.json({ user: result.rows[0] });
  } catch (err) {
    if (err.code === '23505') {
      return res.status(409).json({ error: 'Email-ul este deja folosit.' });
    }
    res.status(500).json({ error: 'Eroare server.' });
  }
});

// DELETE /api/users/:id - sterge utilizator (admin only)
router.delete('/:id', requireAdmin, async (req, res) => {
  try {
    const { id } = req.params;

    // Nu putem sterge propriul cont
    if (parseInt(id) === req.user.id) {
      return res.status(400).json({ error: 'Nu poti sterge propriul cont.' });
    }

    const result = await db.query('DELETE FROM users WHERE id = $1 RETURNING id, full_name, email, fingerprint_slot', [id]);
    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'Utilizator negasit.' });
    }

    if (result.rows[0].fingerprint_slot !== null) {
      await deleteEspFingerprint(result.rows[0].fingerprint_slot);
    }

    // Stergem invitatiile in asteptare pentru acest email, ca adminul sa poata reinvita
    await db.query('DELETE FROM invitations WHERE email = $1 AND used_at IS NULL', [result.rows[0].email]);

    res.json({ message: `User ${result.rows[0].full_name} has been deleted.` });
  } catch (err) {
    res.status(500).json({ error: 'Eroare server.' });
  }
});

// PUT /api/users/:id/password - schimba parola (admin only)
router.put('/:id/password', requireAdmin, async (req, res) => {
  try {
    const { id } = req.params;
    const { password } = req.body;

    if (!password || password.length < 4) {
      return res.status(400).json({ error: 'Parola trebuie sa aiba minim 4 caractere.' });
    }

    const password_hash = await bcrypt.hash(password, 10);
    await db.query('UPDATE users SET password_hash = $1, updated_at = CURRENT_TIMESTAMP WHERE id = $2', [password_hash, id]);

    res.json({ message: 'Parola a fost schimbata.' });
  } catch (err) {
    res.status(500).json({ error: 'Eroare server.' });
  }
});

module.exports = router;
