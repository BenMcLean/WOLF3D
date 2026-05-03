-- WL_ACT2.C:T_Fake - Fake Hitler chase AI
-- Fires projectiles when the player is in line of sight, otherwise moves with dodge-style chasing.

if CheckLine() then
	if US_RndT() < BitShiftLeft(1, 1) then
		local attackState = GetAttackState()
		if attackState ~= nil and attackState ~= "" then
			ChangeState(attackState)
			return
		end
	end
end

if not HasDirection() then
	SelectDodgeDir(true)
	if not HasDirection() then
		return
	end
end

local move = GetSpeed()

while move > 0 do
	local distance = GetDistance()

	if distance == 0 then
		SelectDodgeDir(true)
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

	SelectDodgeDir(true)
	if not HasDirection() then
		return
	end
end
