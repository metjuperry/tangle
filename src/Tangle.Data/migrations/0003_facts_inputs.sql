ALTER TABLE pr ADD COLUMN viewer TEXT NOT NULL DEFAULT '';
CREATE TABLE app_setting (key TEXT PRIMARY KEY, value TEXT NOT NULL);
INSERT INTO app_setting(key,value) VALUES
  ('bot_logins','["dependabot","dependabot[bot]","github-actions","github-actions[bot]"]');
