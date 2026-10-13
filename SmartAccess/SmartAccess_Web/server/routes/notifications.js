const express = require('express');
const db = require('../db');

const router = express.Router();

// GET /api/notifications - notificari din ultimele 30 de zile
router.get('/', async (req, res) => {
  try {
    // Curatenie: sterge notificarile citite mai vechi de 30 de zile
    await db.query(
      "DELETE FROM notifications WHERE user_id = $1 AND is_read = true AND created_at < NOW() - INTERVAL '30 days'",
      [req.user.id]
    );

    const result = await db.query(
      "SELECT * FROM notifications WHERE user_id = $1 AND created_at > NOW() - INTERVAL '30 days' ORDER BY created_at DESC LIMIT 50",
      [req.user.id]
    );

    const unreadCount = await db.query(
      "SELECT COUNT(*) FROM notifications WHERE user_id = $1 AND is_read = false AND created_at > NOW() - INTERVAL '30 days'",
      [req.user.id]
    );

    res.json({
      notifications: result.rows,
      unread_count: parseInt(unreadCount.rows[0].count)
    });
  } catch (err) {
    res.status(500).json({ error: 'Eroare server.' });
  }
});

// PUT /api/notifications/:id/read - marcheaza ca citita
router.put('/:id/read', async (req, res) => {
  try {
    await db.query(
      'UPDATE notifications SET is_read = true WHERE id = $1 AND user_id = $2',
      [req.params.id, req.user.id]
    );
    res.json({ message: 'Notificare marcata ca citita.' });
  } catch (err) {
    res.status(500).json({ error: 'Eroare server.' });
  }
});

// PUT /api/notifications/read-all - marcheaza toate ca citite
router.put('/read-all', async (req, res) => {
  try {
    await db.query(
      'UPDATE notifications SET is_read = true WHERE user_id = $1 AND is_read = false',
      [req.user.id]
    );
    res.json({ message: 'Toate notificarile au fost marcate ca citite.' });
  } catch (err) {
    res.status(500).json({ error: 'Eroare server.' });
  }
});

// DELETE /api/notifications - sterge toate notificarile utilizatorului
router.delete('/', async (req, res) => {
  try {
    await db.query('DELETE FROM notifications WHERE user_id = $1', [req.user.id]);
    res.json({ message: 'Toate notificarile au fost sterse.' });
  } catch (err) {
    res.status(500).json({ error: 'Eroare server.' });
  }
});

module.exports = router;
