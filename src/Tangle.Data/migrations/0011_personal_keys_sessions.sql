CREATE TABLE personal_key(id TEXT PRIMARY KEY,name TEXT NOT NULL,key_hash TEXT NOT NULL UNIQUE,
  created_at TEXT NOT NULL,revoked_at TEXT);
ALTER TABLE session ADD COLUMN repo_id TEXT;
ALTER TABLE session ADD COLUMN excerpt TEXT;
