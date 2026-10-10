CREATE TABLE receipt_round(id TEXT PRIMARY KEY,created_at TEXT NOT NULL);
ALTER TABLE action ADD COLUMN receipt_round_id TEXT REFERENCES receipt_round(id);
ALTER TABLE decision ADD COLUMN receipt_round_id TEXT REFERENCES receipt_round(id);
