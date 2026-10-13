const express = require('express');
const bcrypt = require('bcryptjs');
const db = require('../db');
const { generateToken } = require('../middleware/auth');

const router = express.Router();

// POST /api/auth/login
router.post('/login', async (req, res) => {
  try {
    const { email, password } = req.body;
    if (!email || !password) {
      return res.status(400).json({ error: 'Email si parola sunt obligatorii.' });
    }

    const result = await db.query('SELECT * FROM users WHERE email = $1 AND is_active = true', [email]);
    if (result.rows.length === 0) {
      return res.status(401).json({ error: 'Email sau parola gresita.' });
    }

    const user = result.rows[0];
    const validPassword = await bcrypt.compare(password, user.password_hash);
    if (!validPassword) {
      return res.status(401).json({ error: 'Email sau parola gresita.' });
    }

    const token = generateToken(user);
    res.json({
      token,
      user: {
        id: user.id,
        email: user.email,
        full_name: user.full_name,
        role: user.role
      }
    });
  } catch (err) {
    console.error('Eroare login:', err);
    res.status(500).json({ error: 'Eroare server.' });
  }
});

// GET /api/auth/invite/:token - valideaza token invitatie (public)
router.get('/invite/:token', async (req, res) => {
  try {
    const result = await db.query(
      'SELECT email, full_name FROM invitations WHERE token = $1 AND used_at IS NULL AND expires_at > NOW()',
      [req.params.token]
    );
    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'Link invalid sau expirat.' });
    }
    res.json(result.rows[0]);
  } catch (err) {
    res.status(500).json({ error: 'Eroare server.' });
  }
});

// POST /api/auth/register-from-invite - creare cont din invitatie (public)
router.post('/register-from-invite', async (req, res) => {
  try {
    const { token, password } = req.body;
    if (!token || !password) {
      return res.status(400).json({ error: 'Token si parola sunt obligatorii.' });
    }
    if (password.length < 4) {
      return res.status(400).json({ error: 'Parola trebuie sa aiba minim 4 caractere.' });
    }

    const invResult = await db.query(
      'SELECT * FROM invitations WHERE token = $1 AND used_at IS NULL AND expires_at > NOW()',
      [token]
    );
    if (invResult.rows.length === 0) {
      return res.status(404).json({ error: 'Link invalid sau expirat.' });
    }
    const inv = invResult.rows[0];

    const existing = await db.query('SELECT id FROM users WHERE email = $1', [inv.email]);
    if (existing.rows.length > 0) {
      return res.status(409).json({ error: 'Exista deja un cont cu acest email.' });
    }

    const password_hash = await bcrypt.hash(password, 10);
    const userResult = await db.query(
      'INSERT INTO users (email, password_hash, full_name, role) VALUES ($1, $2, $3, $4) RETURNING id, email, full_name, role',
      [inv.email, password_hash, inv.full_name, 'user']
    );

    await db.query('UPDATE invitations SET used_at = NOW() WHERE id = $1', [inv.id]);

    const user = userResult.rows[0];
    const jwtToken = generateToken(user);
    res.status(201).json({ token: jwtToken, user });
  } catch (err) {
    console.error('Eroare register-from-invite:', err);
    res.status(500).json({ error: 'Eroare server.' });
  }
});

// GET /api/auth/me - info despre utilizatorul curent
router.get('/me', async (req, res) => {
  try {
    const result = await db.query(
      'SELECT id, email, full_name, role, fingerprint_slot, is_active, created_at FROM users WHERE id = $1',
      [req.user.id]
    );
    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'Utilizator negasit.' });
    }
    res.json({ user: result.rows[0] });
  } catch (err) {
    res.status(500).json({ error: 'Eroare server.' });
  }
});

module.exports = router;
