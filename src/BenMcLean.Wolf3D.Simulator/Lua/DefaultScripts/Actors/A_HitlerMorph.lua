-- WL_ACT2.C:A_HitlerMorph - spawns Real Hitler at Mecha-Hitler's position
-- Called from s_mechadie3; the mech continues to s_mechadie4 (static dead state)
-- Original: SpawnNewObj(ob->tilex, ob->tiley, &s_hitlerchase1); new->flags = ob->flags | FL_SHOOTABLE;
-- Hitler spawns directly at chase state (no alert sound), already in attack mode.
SpawnActor("Hitler", GetTileX(), GetTileY(), GetFacing(), "s_hitlerchase1")
