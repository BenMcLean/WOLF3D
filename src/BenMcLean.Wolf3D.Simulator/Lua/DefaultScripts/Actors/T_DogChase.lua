-- WL_ACT2.C:T_DogChase - Fast dodge movement, bite when very close

-- Select direction (dogs always dodge, cannot open doors)
if not HasDirection() then
	SelectDodgeDir(false)  -- Dogs cannot open doors
	if not HasDirection() then
		return  -- Blocked
	end
end

-- Movement loop
local move = GetSpeed()

while move > 0 do
	-- Check if close enough to bite (fixed-point distance)
	local dx = GetPlayerX() - GetX()
	if dx < 0 then dx = -dx end
	dx = dx - move

	if dx <= 0x10000 then  -- MINACTORDIST
		local dy = GetPlayerY() - GetY()
		if dy < 0 then dy = -dy end
		dy = dy - move

		if dy <= 0x10000 then  -- MINACTORDIST
			ChangeState("s_dogjump1")  -- Bite attack!
			return
		end
	end

	local dist = GetDistance()

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

	-- Select new dodge direction
	SelectDodgeDir(false)  -- Dogs cannot open doors

	if not HasDirection() then
		return  -- Blocked
	end
end
