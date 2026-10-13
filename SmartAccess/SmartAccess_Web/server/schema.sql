-- =============================================
-- SmartAccess - Schema Baza de Date
-- =============================================

-- Cream baza de date (rulam de pe postgres default)
DROP DATABASE IF EXISTS smartaccess;
CREATE DATABASE smartaccess;

-- Ne conectam la baza nou creata
\c smartaccess

-- =============================================
-- TABELA: users
-- =============================================
CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    full_name VARCHAR(255) NOT NULL,
    role VARCHAR(20) NOT NULL DEFAULT 'user' CHECK (role IN ('admin', 'user')),
    fingerprint_slot INTEGER UNIQUE CHECK (fingerprint_slot >= 0 AND fingerprint_slot <= 9),
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- =============================================
-- TABELA: access_log
-- =============================================
CREATE TABLE access_log (
    id SERIAL PRIMARY KEY,
    user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
    access_type VARCHAR(50) NOT NULL CHECK (access_type IN ('fingerprint', 'pin', 'app_unlock', 'unknown')),
    result VARCHAR(20) NOT NULL CHECK (result IN ('granted', 'denied')),
    details TEXT,
    user_name VARCHAR(255),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- =============================================
-- TABELA: notifications
-- =============================================
CREATE TABLE notifications (
    id SERIAL PRIMARY KEY,
    user_id INTEGER REFERENCES users(id) ON DELETE CASCADE,
    type VARCHAR(50) NOT NULL CHECK (type IN ('access_denied', 'system_alert', 'info')),
    message TEXT NOT NULL,
    is_read BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- =============================================
-- INDEX-URI pentru performanta
-- =============================================
CREATE INDEX idx_access_log_created_at ON access_log(created_at DESC);
CREATE INDEX idx_access_log_user_id ON access_log(user_id);
CREATE INDEX idx_notifications_user_read ON notifications(user_id, is_read);
