-- WL_ACT2.C:T_Launch - fire a projectile toward the player.
-- Death Knight offsets the two rocket shots slightly left/right; Angel and Wilhelm fire straight.

local angle = GetAngleToPlayer()
local actorType = GetActorType()

if actorType == "Death" then
	local stateName = GetCurrentStateName()
	if stateName == "s_deathshoot2" then
		angle = (angle - 4) % 360
	else
		angle = (angle + 4) % 360
	end
end

if actorType == "Angel" then
	SpawnProjectile("spark", angle)
	PlayLocalSound("ANGELFIRESND")
elseif actorType == "Death" then
	SpawnProjectile("hrocket", angle)
	PlayLocalSound("KNIGHTMISSILESND")
else
	SpawnProjectile("rocket", angle)
	PlayLocalSound("MISSILEFIRESND")
end
