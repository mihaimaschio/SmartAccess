const express = require('express');
const db = require('../db');

const router = express.Router();

// GET /api/access-log - istoric acces
router.get('/', async (req, res) => {
  try {
    const { limit = 50, offset = 0, user_id, result: accessResult } = req.query;
    
    let query = `
      SELECT a.*, COALESCE(u.full_name, a.user_name) as user_name
      FROM access_log a
      LEFT JOIN users u ON a.user_id = u.id
    `;
    const params = [];
    const conditions = [];
    
    if (user_id) {
      conditions.push(`a.user_id = $${params.length + 1}`);
      params.push(user_id);
    }
    
    if (accessResult) {
      conditions.push(`a.result = $${params.length + 1}`);
      params.push(accessResult);
    }
    
    if (conditions.length > 0) {
      query += ' WHERE ' + conditions.join(' AND ');
    }
    
    query += ` ORDER BY a.created_at DESC LIMIT $${params.length + 1} OFFSET $${params.length + 2}`;
    params.push(parseInt(limit), parseInt(offset));
    
    const result = await db.query(query, params);
    
    // Total count
    let countQuery = 'SELECT COUNT(*) FROM access_log a';
    if (conditions.length > 0) {
      countQuery += ' WHERE ' + conditions.join(' AND ');
    }
    const countResult = await db.query(countQuery, params.slice(0, -2));
    
    res.json({ 
      logs: result.rows,
      total: parseInt(countResult.rows[0].count)
    });
  } catch (err) {
    console.error('Eroare access log:', err);
    res.status(500).json({ error: 'Eroare server.' });
  }
});

// GET /api/access-log/recent - ultimele 5 accesari (pentru dashboard)
router.get('/recent', async (req, res) => {
  try {
    const result = await db.query(`
      SELECT a.*, COALESCE(u.full_name, a.user_name) as user_name
      FROM access_log a
      LEFT JOIN users u ON a.user_id = u.id
      ORDER BY a.created_at DESC 
      LIMIT 5
    `);
    res.json({ logs: result.rows });
  } catch (err) {
    res.status(500).json({ error: 'Eroare server.' });
  }
});

module.exports = router;
