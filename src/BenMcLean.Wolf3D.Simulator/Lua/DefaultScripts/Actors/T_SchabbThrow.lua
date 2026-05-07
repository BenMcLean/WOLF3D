-- WL_ACT2.C:T_SchabbThrow - Dr. Schabbs throws a hypodermic needle toward the player
-- Fires as Action on s_schabbshoot2; spawns needleobj at s_needle1 with speed=0x2000
-- Original: GetNewActor(), new->obclass = needleobj, new->angle = iangle, PlaySoundLocActor(SCHABBSTHROWSND)
local angle = GetAngleToPlayer()
SpawnProjectile("needle", angle)
PlayLocalSound(ResolveSound("SCHABBSTHROWSND", "D_COCTHRSND"))
