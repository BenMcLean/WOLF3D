-- WL_ACT2.C:T_Bite (lines 4270-4305)
-- Dog bite attack: check distance, roll for hit, apply damage

PlayLocalDigiSound("DOGATTACKSND")

-- WL_ACT2.C:4281-4291 - Check if adjacent to player (within 1 tile)
local dx = GetPlayerX() - GetX()
if dx < 0 then dx = -dx end
dx = dx - 0x10000  -- TILEGLOBAL
if dx > 0x10000 then  -- MINACTORDIST
	return
end

local dy = GetPlayerY() - GetY()
if dy < 0 then dy = -dy end
dy = dy - 0x10000  -- TILEGLOBAL
if dy > 0x10000 then  -- MINACTORDIST
	return
end

-- WL_ACT2.C:4293 - 70% hit chance (180/256)
if US_RndT() < 180 then
	-- WL_ACT2.C:4297 - Damage: US_RndT() >> 4 (0-15)
	DamagePlayer(BitShiftRight(US_RndT(), 4))
end
