-- WL_ACT2.C:T_Chase - Chase and attack player when in range

local dodge = false

-- WL_ACT2.C:3757 - Check line of sight to player (not CheckSight - no FOV restriction in chase)
if CheckLine() then
	-- Calculate distance
	local dx = GetPlayerTileX() - GetTileX()
	local dy = GetPlayerTileY() - GetTileY()
	if dx < 0 then dx = -dx end
	if dy < 0 then dy = -dy end
	local dist = dx
	if dy > dx then dist = dy end

	-- Calculate attack chance based on distance
	local chance = 0
	if dist == 0 or (dist == 1 and GetDistance() < 0x4000) then
		chance = 300  -- Very close - always attack
	else
		chance = BitShiftLeft(1, 4) / dist  -- tics<<4 / dist (tics hardcoded as 1)
	end

	-- Random chance to attack
	if US_RndT() < chance then
		-- WL_ACT2.C:3772-3828 - Enter attack state based on actor type.
		-- Use data-driven AttackState from XML actor definition instead of hardcoded names.
		local attackState = GetAttackState()
		if attackState ~= nil and attackState ~= "" then
			ChangeState(attackState)
			return
		end
	end

	dodge = true  -- Saw player, use evasive movement
end

-- Select movement direction
if not HasDirection() then
	if dodge then
		SelectDodgeDir(true)  -- Guards can open doors
	else
		SelectChaseDir(true)  -- Guards can open doors
	end

	if not HasDirection() then
		return  -- Blocked in, can't move
	end
end

-- Movement loop
local move = GetSpeed()

while move > 0 do
	local dist = GetDistance()

	-- If distance is 0, we need to select a new direction
	-- This can happen when transitioning from other states (patrol, shoot, etc.)
	if dist == 0 then
		if dodge then
			SelectDodgeDir(true)  -- Guards can open doors
		else
			SelectChaseDir(true)  -- Guards can open doors
		end

		if not HasDirection() then
			return  -- Blocked
		end

		dist = GetDistance()
		if dist == 0 then
			return  -- Still no distance, blocked
		end
	end

	-- Check if waiting for door
	if dist < 0 then
		local doorIndex = -(dist + 1)
		OpenDoor(doorIndex)
		if not IsDoorOpen(doorIndex) then
			return  -- Still waiting
		end
		SetDistance(0x10000)  -- Door open, continue
		dist = 0x10000
	end

	-- Move partial distance
	if move < dist then
		MoveObj(move)
		break
	end

	-- Reached goal tile, snap to center
	local tileX = GetTileX()
	local tileY = GetTileY()
	local centerX = BitShiftLeft(tileX, 16) + 0x8000
	local centerY = BitShiftLeft(tileY, 16) + 0x8000
	SetPosition(centerX, centerY)

	move = move - dist

	-- Select new direction for next tile
	if dodge then
		SelectDodgeDir(true)  -- Guards can open doors
	else
		SelectChaseDir(true)  -- Guards can open doors
	end

	if not HasDirection() then
		return  -- Blocked
	end
end
