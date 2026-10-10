-- Refresh cached events once to include commit author names, retaining viewer scope identity.
UPDATE sync_state SET cursor=NULL,last_run_at=NULL;
