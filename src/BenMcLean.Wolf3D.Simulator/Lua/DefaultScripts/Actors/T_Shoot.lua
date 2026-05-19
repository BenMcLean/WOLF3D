-- WL_ACT2.C:T_Shoot (lines 4150-4218)
-- Attempt to shoot the player based on line of sight and distance

-- WL_ACT2.C:4157-4158 - Check if player is in the same area
-- (skipped - areabyplayer system not yet implemented)

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
-- switch(ob->obclass): only ssobj (SS) and bossobj (Hans) get this bonus.
-- N3D equivalents: Antelope (ssobj), Carl the Camel (bossobj).
local actorType = GetActorType()
if actorType == "SS" or actorType == "Hans"
	or actorType == "Antelope" or actorType == "Carl the Camel" then
	dist = (dist * 2) / 3
end

-- WL_ACT2.C:4181-4194 - Calculate hit chance based on distance.
-- Original branches on two conditions:
--   thrustspeed >= RUNSPEED: base 160 (running player, harder to hit)
--   thrustspeed <  RUNSPEED: base 256 (standing player, easier to hit)
--   ob->flags & FL_VISABLE:  dist*16 penalty (player can see enemy, more dodge room)
--   not FL_VISABLE:          dist*8  penalty (enemy off-screen, less dodge room)
--
-- INTENTIONAL DEVIATION FROM ORIGINAL:
-- We always use the most player-advantageous branch: base 160 with dist*16 penalty
-- (running + visible). Rationale: in VR, teleport-locomotion players cannot naturally
-- benefit from the running dodge bonus, so we apply it unconditionally so that teleport
-- and smooth-motion players receive equal treatment. The FL_VISABLE distinction is also
-- dropped for the same reason (VR has no equivalent to a 2.5D sprite visibility check).
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

-- WL_ACT2.C:4219-4256 - Play appropriate firing sound.
-- WL_ACT2.C:switch(ob->obclass) maps enemy class to sound.
local shootSound
if actorType == "SS" or actorType == "Antelope" then
	-- case ssobj
	shootSound = ResolveSound("SSFIRESND", "D_SPITSND")
elseif actorType == "Hans" or actorType == "MechaHitler" or actorType == "Hitler"
	or actorType == "Carl the Camel" or actorType == "Burt the Bear"
	or actorType == "Trans" or actorType == "Wilhelm" or actorType == "Death" then
	-- case bossobj / mechahitlerobj / realhitlerobj (+ Spear bosses that fire similarly)
	shootSound = ResolveSound("BOSSFIRESND", "D_SPITSND")
elseif actorType == "Schabbs" or actorType == "Kerry the Kangaroo" then
	-- case schabbobj
	shootSound = ResolveSound("SCHABBSTHROWSND", "D_COCTHRSND")
elseif actorType == "FakeHitler" then
	-- case fakeobj
	shootSound = ResolveSound("FLAMETHROWERSND", "D_SPITSND")
elseif actorType == "Giftmacher" or actorType == "FatFace" or actorType == "Ernie the Elephant" then
	-- case giftobj / fatobj
	shootSound = ResolveSound("MISSILEFIRESND", "D_COCTHRSND")
else
	-- default: guards, officers, mutants, Gretel, etc.
	shootSound = ResolveSound("NAZIFIRESND", "D_SPITSND")
end
PlayLocalSound(shootSound)
