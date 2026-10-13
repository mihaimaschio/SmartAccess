require('dotenv').config();
const express = require('express');
const cors = require('cors');
const path = require('path');
const db = require('./db');
const { authenticate } = require('./middleware/auth');

const app = express();
const PORT = process.env.PORT || 3000;

// Middleware global
app.use(cors());
app.use(express.json());
app.use(express.static(path.join(__dirname, '..', 'public')));

// Rute publice
const authRoutes = require('./routes/auth');
app.use('/api/auth', (req, res, next) => {
  // Login si register sunt publice, /me necesita auth
  if (req.path === '/me') return authenticate(req, res, next);
  next();
}, authRoutes);

// Rute protejate (necesita JWT)
app.use('/api/users', authenticate, require('./routes/users'));
app.use('/api/members', authenticate, require('./routes/members'));
app.use('/api/esp', authenticate, require('./routes/esp'));
app.use('/api/access-log', authenticate, require('./routes/access'));
app.use('/api/notifications', authenticate, require('./routes/notifications'));
app.use('/api/settings', authenticate, require('./routes/settings'));
app.use('/api/invitations', authenticate, require('./routes/invitations'));

// Servire pagini HTML
// Daca path-ul nu e API si nu e fisier static, servim index.html (login)
app.get('*', (req, res) => {
  // Verificam daca fisierul HTML exista
  const htmlFile = path.join(__dirname, '..', 'public', req.path);
  res.sendFile(path.join(__dirname, '..', 'public', 'index.html'));
});

// Pornire server
const espRoutes = require('./routes/esp');
const server = app.listen(PORT, () => {
  console.log(`SmartAccess - server pornit pe portul ${PORT}`);
  console.log(`ESP: ${process.env.ESP_IP || 'neconfigurat'}`);
  console.log(`Baza de date: ${process.env.DB_NAME}@${process.env.DB_HOST}`);
  espRoutes.startPolling();
});

server.on('error', (err) => {
  if (err.code === 'EADDRINUSE') {
    console.error(`Portul ${PORT} este deja folosit - astept eliberarea...`);
    setTimeout(() => process.exit(1), 500);
  } else {
    throw err;
  }
});

// Inchidere curata, ca node --watch sa poata reporni serverul
process.on('SIGTERM', () => { server.close(() => process.exit(0)); });
process.on('SIGINT',  () => { server.close(() => process.exit(0)); });
