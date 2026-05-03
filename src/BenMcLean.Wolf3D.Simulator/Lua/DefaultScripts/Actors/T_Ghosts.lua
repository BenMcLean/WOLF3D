-- WL_ACT2.C:T_Ghosts - direct chase movement for Pac-Man ghosts.

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
		MoveObj(move)
		break
	end

	local tileX = GetTileX()
	local tileY = GetTileY()
	local centerX = BitShiftLeft(tileX, 16) + 0x8000
	local centerY = BitShiftLeft(tileY, 16) + 0x8000
	SetPosition(centerX, centerY)

	move = move - dist

	SelectChaseDir(false)
	if not HasDirection() then
		return
	end
end
