-- WL_ACT2.C:T_Shoot (lines 4150-4218)
-- Attempt to shoot the player based on line of sight and distance

-- WL_ACT2.C:4157-4158 - Check if player is in the same area
-- (skipped - assume player is always reachable for now)

-- WL_ACT2.C:4160-4168 - Check line of sight
if not CheckLine() then
	-- Player is behind a wall, can't shoot
	return
end

-- WL_ACT2.C:4171-4173 - Calculate distance to player
local dx = GetPlayerTileX() - GetTileX()
local dy = GetPlayerTileY() - GetTileY()
if dx < 0 then dx = -dx end
if dy < 0 then dy = -dy end
local dist = dx
if dy > dx then dist = dy end

-- WL_ACT2.C:4177-4179 - SS and bosses are better shots (2/3 distance)
local actorType = GetActorType()
if actorType == "SS" or actorType == "Hans" then
	dist = (dist * 2) / 3
end

-- WL_ACT2.C:4181-4194 - Calculate hit chance based on distance and player speed
-- TODO: Check player speed when that's implemented
-- For now, assume player is moving and visible
local hitchance = 160 - (dist * 16)

-- WL_ACT2.C:4196-4210 - Roll for hit and calculate damage
if US_RndT() < hitchance then
	-- Hit! Calculate damage based on distance
	local damage = 0
	if dist < 2 then
		damage = US_RndT() / 4  -- Close range
	elseif dist < 4 then
		damage = US_RndT() / 8  -- Medium range
	else
		damage = US_RndT() / 16  -- Long range
	end

	DamagePlayer(damage)
end

-- WL_ACT2.C:4219-4256 - Play appropriate firing sound based on actor type
if actorType == "SS" then
	PlayLocalDigiSound("SSFIRESND")
elseif actorType == "Hans" then
	PlayLocalDigiSound("BOSSFIRESND")
else
	-- Guards, officers, and other enemies use NAZIFIRESND
	PlayLocalDigiSound("NAZIFIRESND")
end
