const express = require('express');
const crypto = require('crypto');
const db = require('../db');
const { requireAdmin } = require('../middleware/auth');
const { sendInviteEmail } = require('../email');

const router = express.Router();

// POST /api/invitations - creeaza link de invitatie si trimite email (admin only)
router.post('/', requireAdmin, async (req, res) => {
  try {
    const { email, full_name } = req.body;
    if (!email || !full_name) {
      return res.status(400).json({ error: 'Email and name are required.' });
    }

    const existing = await db.query('SELECT id FROM users WHERE email = $1', [email]);
    if (existing.rows.length > 0) {
      return res.status(409).json({ error: 'An account with this email already exists.' });
    }

    // Stergem invitatiile nefolosite pentru acest email inainte de a crea una noua
    await db.query('DELETE FROM invitations WHERE email = $1 AND used_at IS NULL', [email]);

    const token = crypto.randomBytes(32).toString('hex');
    const expiresAt = new Date(Date.now() + 48 * 60 * 60 * 1000); // 48h

    await db.query(
      'INSERT INTO invitations (email, full_name, token, invited_by, expires_at) VALUES ($1, $2, $3, $4, $5)',
      [email, full_name, token, req.user.id, expiresAt]
    );

    const appUrl = process.env.APP_URL || 'http://localhost:3000';
    const inviteUrl = `${appUrl}/register.html?token=${token}`;

    let emailSent = false;
    try {
      emailSent = await sendInviteEmail({
        to: email,
        full_name,
        inviteUrl,
        invitedBy: req.user.full_name,
      });
    } catch (emailErr) {
      console.error('Failed to send invite email:', emailErr.message);
    }

    res.status(201).json({ token, email_sent: emailSent });
  } catch (err) {
    console.error('Error creating invitation:', err);
    res.status(500).json({ error: 'Server error.' });
  }
});

// GET /api/invitations - lista invitatiilor active (admin only)
router.get('/', requireAdmin, async (req, res) => {
  try {
    const result = await db.query(
      `SELECT i.id, i.email, i.full_name, i.expires_at, i.created_at
       FROM invitations i
       WHERE i.used_at IS NULL AND i.expires_at > NOW()
       ORDER BY i.created_at DESC`
    );
    res.json({ invitations: result.rows });
  } catch (err) {
    res.status(500).json({ error: 'Server error.' });
  }
});

// DELETE /api/invitations/:id - revoca o invitatie (admin only)
router.delete('/:id', requireAdmin, async (req, res) => {
  try {
    await db.query('DELETE FROM invitations WHERE id = $1', [req.params.id]);
    res.json({ message: 'Invitation revoked.' });
  } catch (err) {
    res.status(500).json({ error: 'Server error.' });
  }
});

module.exports = router;
