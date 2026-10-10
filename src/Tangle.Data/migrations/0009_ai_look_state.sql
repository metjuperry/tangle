CREATE TABLE ai_look(job TEXT NOT NULL,target TEXT NOT NULL,state_hash TEXT NOT NULL,
  call_id TEXT NOT NULL REFERENCES ai_call(id),PRIMARY KEY(job,target,state_hash));
