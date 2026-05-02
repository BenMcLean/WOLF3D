-- WL_ACT2.C:A_HitlerMorph - spawns Real Hitler at Mecha-Hitler's position
-- Called from s_mechadie3; the mech continues to s_mechadie4 (static dead state)
SpawnActor("Hitler", GetTileX(), GetTileY(), GetFacing())
