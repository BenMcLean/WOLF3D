-- WL_ACT2.C:T_UShoot - Ubermutant uses the normal boss hitscan plus point-blank impact damage.

if CheckLine() then
	local dx = GetPlayerTileX() - GetTileX()
	local dy = GetPlayerTileY() - GetTileY()
	if dx < 0 then dx = -dx end
	if dy < 0 then dy = -dy end
	local dist = dx
	if dy > dx then dist = dy end

	local hitchance = 256 - (dist * 16)
	if US_RndT() < hitchance then
		local damage = 0
		if dist < 2 then
			damage = US_RndT() / 4
		elseif dist < 4 then
			damage = US_RndT() / 8
		else
			damage = US_RndT() / 16
		end

		DamagePlayer(damage)
	end
end

local shootSound = GetShootSound()
if shootSound ~= nil and shootSound ~= "" then
	if shootSound == "NAZIFIRESND" or shootSound == "SSFIRESND" or shootSound == "BOSSFIRESND" then
		shootSound = ResolveSound(shootSound, "D_SPITSND")
	elseif shootSound == "MISSILEFIRESND" or shootSound == "SCHABBSTHROWSND" then
		shootSound = ResolveSound(shootSound, "D_COCTHRSND")
	end
	PlayLocalSound(shootSound)
else
	PlayLocalSound(ResolveSound("NAZIFIRESND", "D_SPITSND"))
end

if CalculateDistanceToPlayer() <= 1 then
	DamagePlayer(10)
end
