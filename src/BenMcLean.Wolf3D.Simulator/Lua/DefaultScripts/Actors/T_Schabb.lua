-- WL_ACT2.C:T_Schabb - Dr. Schabbs chase AI
-- Difference from T_Chase: attack uses tics<<3 threshold (not distance-based);
-- runs away (SelectRunDir) when player is within 4 tiles.

local dodge = false
local dx = GetPlayerTileX() - GetTileX()
local dy = GetPlayerTileY() - GetTileY()
if dx < 0 then dx = -dx end
if dy < 0 then dy = -dy end
local dist = dx
if dy > dx then dist = dy end

-- WL_ACT2.C:T_Schabb - attack if line of sight and random threshold (tics<<3 = 8 with tics=1)
if CheckLine() then
	if US_RndT() < BitShiftLeft(1, 3) then
		local attackState = GetAttackState()
		if attackState ~= nil and attackState ~= "" then
			ChangeState(attackState)
			return
		end
	end
	dodge = true
end

if not HasDirection() then
	-- WL_STATE.C:SelectRunDir when dist < 4 (flee); otherwise dodge or chase
	if dist < 4 then
		SelectRunDir(true)
	elseif dodge then
		SelectDodgeDir(true)
	else
		SelectChaseDir(true)
	end

	if not HasDirection() then
		return
	end
end

local move = GetSpeed()

while move > 0 do
	local distance = GetDistance()

	if distance == 0 then
		if dist < 4 then
			SelectRunDir(true)
		elseif dodge then
			SelectDodgeDir(true)
		else
			SelectChaseDir(true)
		end

		if not HasDirection() then
			return
		end

		distance = GetDistance()
		if distance == 0 then
			return
		end
	end

	if distance < 0 then
		local doorIndex = -(distance + 1)
		OpenDoor(doorIndex)
		if not IsDoorOpen(doorIndex) then
			return
		end
		SetDistance(0x10000)
		distance = 0x10000
	end

	if move < distance then
		MoveObj(move)
		break
	end

	local tileX = GetTileX()
	local tileY = GetTileY()
	local centerX = BitShiftLeft(tileX, 16) + 0x8000
	local centerY = BitShiftLeft(tileY, 16) + 0x8000
	SetPosition(centerX, centerY)

	move = move - distance

	-- Recalculate dist after moving (tile coordinates may have changed)
	dx = GetPlayerTileX() - GetTileX()
	dy = GetPlayerTileY() - GetTileY()
	if dx < 0 then dx = -dx end
	if dy < 0 then dy = -dy end
	dist = dx
	if dy > dx then dist = dy end

	if dist < 4 then
		SelectRunDir(true)
	elseif dodge then
		SelectDodgeDir(true)
	else
		SelectChaseDir(true)
	end

	if not HasDirection() then
		return
	end
end
