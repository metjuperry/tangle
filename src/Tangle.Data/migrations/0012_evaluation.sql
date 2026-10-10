ALTER TABLE ai_call ADD COLUMN input_json TEXT;
ALTER TABLE verdict ADD COLUMN input_json TEXT;
CREATE TABLE pair_prediction(id TEXT PRIMARY KEY,a_ref TEXT NOT NULL,b_ref TEXT NOT NULL,
  decider TEXT NOT NULL,related REAL NOT NULL,reason TEXT,input_json TEXT,created_at TEXT NOT NULL,
  call_id TEXT REFERENCES ai_call(id));
CREATE TABLE evaluation_result(example_id TEXT NOT NULL,decider TEXT NOT NULL,predicted TEXT NOT NULL,
  probability REAL NOT NULL,cost REAL NOT NULL,latency_ms INTEGER NOT NULL,created_at TEXT NOT NULL,
  PRIMARY KEY(example_id,decider));
INSERT INTO app_setting(key,value) VALUES ('ai_verdicts','haiku-5-5'),('ai_pairs','haiku-5-5'),('ai_explain','haiku-5-5');
-- Existing decided AI strings retain their original confidence; no historical input is invented.
INSERT INTO pair_prediction(id,a_ref,b_ref,decider,related,reason,created_at)
SELECT 'legacy:'||id,a_ref,b_ref,'haiku-5-5',confidence,reason,created_at FROM link
WHERE source='ai' AND confidence IS NOT NULL;
