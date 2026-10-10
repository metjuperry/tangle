ALTER TABLE ai_call ADD COLUMN status TEXT NOT NULL DEFAULT 'completed';
ALTER TABLE ai_call ADD COLUMN reserved_cost REAL NOT NULL DEFAULT 0;
ALTER TABLE ai_call ADD COLUMN error TEXT;
CREATE TABLE ai_cache (input_hash TEXT PRIMARY KEY, created_at TEXT NOT NULL);
INSERT INTO app_setting(key,value) VALUES ('ai_budget','1.00');
