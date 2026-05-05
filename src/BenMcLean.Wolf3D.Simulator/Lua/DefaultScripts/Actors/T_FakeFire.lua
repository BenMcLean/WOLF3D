-- WL_ACT2.C:T_FakeFire - Fake Hitler launches a fireball toward the player
local angle = GetAngleToPlayer()
SpawnProjectile("fire", angle)
PlayLocalSound("FLAMETHROWERSND")
