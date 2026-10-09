CREATE TABLE organization (id TEXT PRIMARY KEY, watched INTEGER NOT NULL DEFAULT 1);
CREATE TABLE repo (id TEXT PRIMARY KEY, watched INTEGER NOT NULL DEFAULT 1,
  ai_allowed INTEGER NOT NULL DEFAULT 0 CHECK (ai_allowed IN (0,1)));
CREATE TABLE pr (id TEXT PRIMARY KEY, repo_id TEXT NOT NULL REFERENCES repo(id),
  number INTEGER NOT NULL, title TEXT, body TEXT, author TEXT, is_mine INTEGER,
  state TEXT, is_draft INTEGER, head_ref TEXT, base_ref TEXT,
  created_at TEXT, updated_at TEXT, closed_at TEXT,
  additions INTEGER, deletions INTEGER, changed_files INTEGER,
  mergeable TEXT, review_decision TEXT, checks_state TEXT, content_hash TEXT, batch_id TEXT,
  UNIQUE(repo_id, number));
CREATE TABLE pr_file (pr_id TEXT REFERENCES pr(id), path TEXT, PRIMARY KEY(pr_id,path));
CREATE TABLE pr_event (pr_id TEXT REFERENCES pr(id), at TEXT, actor TEXT, kind TEXT, detail TEXT);
CREATE INDEX ix_pr_event_pr ON pr_event(pr_id);
CREATE TABLE pr_review_request (pr_id TEXT REFERENCES pr(id), reviewer TEXT, kind TEXT,
  PRIMARY KEY(pr_id,reviewer,kind));
CREATE TABLE review_thread (id TEXT PRIMARY KEY, pr_id TEXT REFERENCES pr(id), path TEXT,
  is_resolved INTEGER, last_actor TEXT, last_at TEXT, excerpt TEXT);
CREATE INDEX ix_review_thread_pr ON review_thread(pr_id);
CREATE TABLE fact (pr_id TEXT PRIMARY KEY REFERENCES pr(id), turn TEXT, turn_reason TEXT,
  age_days INTEGER, idle_days INTEGER, behind_by INTEGER, conflicting INTEGER,
  size TEXT, origin TEXT, computed_at TEXT);
CREATE TABLE verdict (pr_id TEXT REFERENCES pr(id), decider TEXT, action TEXT, confidence REAL,
  reason TEXT, intent TEXT, priority INTEGER, input_hash TEXT, created_at TEXT,
  PRIMARY KEY(pr_id,decider,input_hash));
CREATE TABLE session (id TEXT PRIMARY KEY, path TEXT, cwd TEXT, git_branch TEXT,
  started_at TEXT, pr_id TEXT, matched_by TEXT);
CREATE TABLE sticky (id TEXT PRIMARY KEY, title TEXT, color TEXT, archived_at TEXT);
CREATE TABLE link (id TEXT PRIMARY KEY, a_ref TEXT NOT NULL, b_ref TEXT NOT NULL,
  source TEXT NOT NULL, rule TEXT, status TEXT NOT NULL, confidence REAL, reason TEXT, label TEXT,
  created_at TEXT, decided_at TEXT, UNIQUE(a_ref,b_ref), CHECK(a_ref < b_ref),
  CHECK(source IN ('rule','ai','me')), CHECK(status IN ('suggested','tied','cut')));
CREATE TABLE note (id TEXT PRIMARY KEY, target_ref TEXT, text TEXT, created_at TEXT);
CREATE TABLE position (node_ref TEXT PRIMARY KEY, x REAL, y REAL, moved_by_me INTEGER);
CREATE TABLE action (id TEXT PRIMARY KEY, pr_id TEXT, kind TEXT, payload TEXT, status TEXT,
  created_at TEXT, applied_at TEXT, result TEXT);
CREATE TABLE sync_state (scope TEXT PRIMARY KEY, cursor TEXT, last_run_at TEXT);
CREATE TABLE ai_call (id TEXT PRIMARY KEY, pr_id TEXT, decider TEXT, input_hash TEXT,
  input_tokens INTEGER, output_tokens INTEGER, cost REAL, latency_ms INTEGER, created_at TEXT);
