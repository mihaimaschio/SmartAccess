-- =============================================
-- SmartAccess - Date Initiale (Seed)
-- Rulam DUPA schema.sql, pe baza smartaccess
-- =============================================

-- Cont admin initial
-- Email: admin@smartaccess.local
-- Parola: admin123 (hash bcrypt)
INSERT INTO users (email, password_hash, full_name, role)
VALUES (
    'admin@smartaccess.local',
    '$2a$10$NqJw8XqPossAcRyJ/KA8oebbbTaENq1RFNWgCVu7ZaMLVSpv9y2ca',
    'Administrator',
    'admin'
) ON CONFLICT (email) DO NOTHING;

-- Notificare de bun venit
INSERT INTO notifications (user_id, type, message)
SELECT id, 'info', 'Bine ai venit in SmartAccess! Sistemul este configurat si gata de utilizare.'
FROM users WHERE email = 'admin@smartaccess.local';
