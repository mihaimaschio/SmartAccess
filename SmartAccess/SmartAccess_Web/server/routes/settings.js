const express = require('express');
require('dotenv').config();

const router = express.Router();

// GET /api/settings - expune clientilor configuratia de care au nevoie.
// Singura setare ramasa este adresa fluxului video; se modifica doar din
// backend (variabila CAMERA_URL din .env), nu din interfata.
router.get('/', (req, res) => {
  res.json({
    settings: {
      camera_url: process.env.CAMERA_URL || ''
    }
  });
});

module.exports = router;
