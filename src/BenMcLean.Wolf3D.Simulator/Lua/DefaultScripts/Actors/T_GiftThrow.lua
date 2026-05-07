-- WL_ACT2.C:T_GiftThrow - Giftmacher and FatFace fire a rocket toward the player
-- Fires as Action on s_giftshoot2 and s_fatshoot2; spawns rocketobj at s_rocket with speed=0x2000
-- Original: GetNewActor(), new->obclass = rocketobj, new->angle = iangle, PlaySoundLocActor(MISSILEFIRESND)
local angle = GetAngleToPlayer()
SpawnProjectile("rocket", angle)
PlayLocalSound(ResolveSound("MISSILEFIRESND", "D_COCTHRSND"))
