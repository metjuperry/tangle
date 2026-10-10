CREATE TABLE decision(id TEXT PRIMARY KEY,pr_id TEXT NOT NULL REFERENCES pr(id),
  choice TEXT NOT NULL,action_id TEXT REFERENCES action(id),created_at TEXT NOT NULL);
