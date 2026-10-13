const { Pool } = require('pg');
require('dotenv').config();

const pool = new Pool({
  host: process.env.DB_HOST || 'localhost',
  port: parseInt(process.env.DB_PORT || '5432'),
  database: process.env.DB_NAME || 'smartaccess',
  user: process.env.DB_USER || 'postgres',
  password: process.env.DB_PASS || 'postgres',
});

pool.on('error', (err) => {
  console.error('Eroare neasteptata la conexiunea PostgreSQL:', err);
});

// Test conexiune la pornire
pool.query('SELECT NOW()')
  .then(() => console.log('Conectat la PostgreSQL'))
  .catch(err => console.error('Eroare PostgreSQL:', err.message));

module.exports = pool;
