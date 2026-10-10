ALTER TABLE ai_call ADD COLUMN job TEXT;
ALTER TABLE ai_call ADD COLUMN targets TEXT;
ALTER TABLE ai_call ADD COLUMN outcome TEXT;
UPDATE ai_call SET pr_id=(SELECT v.pr_id FROM verdict v WHERE v.input_hash=ai_call.input_hash LIMIT 1),
  job='verdict',
  outcome=(SELECT v.action||': '||v.reason FROM verdict v WHERE v.input_hash=ai_call.input_hash LIMIT 1)
WHERE EXISTS(SELECT 1 FROM verdict v WHERE v.input_hash=ai_call.input_hash);
