-- WL_ACT2.C:T_Ghosts - direct chase movement for Pac-Man ghosts.

local function touchPlayer(oldX, oldY, oldDistance)
	local dx = GetX() - GetPlayerX()
	if dx < 0 then dx = -dx end
	if dx > 0x10000 then
		return false
	end

	local dy = GetY() - GetPlayerY()
	if dy < 0 then dy = -dy end
	if dy > 0x10000 then
		return false
	end

	-- WL_STATE.C:MoveObj - ghost/spectre contact damage uses TakeDamage(tics * 2).
	-- This simulator advances actor logic at 1 tic per update, so that is 2 damage.
	-- On baby difficulty, the simulator quarters incoming damage; bump to 4 so
	-- ghost contact still produces a visible 1-point hit like the original feels.
	local damage = 2
	if GetValue("Difficulty") == 0 then
		damage = 4
	end

	local oldHealth = GetValue("Health")
	DamagePlayer(damage)
	if GetValue("Health") < oldHealth then
		PlayLocalSound("NAZIHITPLAYERSND")
	end
	SetPosition(oldX, oldY)
	SetDistance(oldDistance)
	return true
end

if not HasDirection() then
	SelectChaseDir(false)
	if not HasDirection() then
		return
	end
end

local move = GetSpeed()

while move > 0 do
	local dist = GetDistance()

	if move < dist then
		local oldX = GetX()
		local oldY = GetY()
		local oldDistance = dist
		MoveObj(move)
		if touchPlayer(oldX, oldY, oldDistance) then
			return
		end
		break
	end

	local oldX = GetX()
	local oldY = GetY()
	local oldDistance = dist
	local tileX = GetTileX()
	local tileY = GetTileY()
	local centerX = BitShiftLeft(tileX, 16) + 0x8000
	local centerY = BitShiftLeft(tileY, 16) + 0x8000
	SetPosition(centerX, centerY)
	if touchPlayer(oldX, oldY, oldDistance) then
		return
	end

	move = move - dist

	SelectChaseDir(false)
	if not HasDirection() then
		return
	end
end
