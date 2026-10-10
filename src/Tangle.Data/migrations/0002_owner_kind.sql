ALTER TABLE organization ADD COLUMN kind TEXT NOT NULL DEFAULT 'org' CHECK(kind IN ('org','user'));
