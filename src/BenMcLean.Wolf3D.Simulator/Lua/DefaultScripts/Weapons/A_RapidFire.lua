-- Rapid fire - loop back to firing frame if button held and have ammo
-- WL_AGENT.C case 3 (machine gun) and case 4 (chain gun)
if HasAmmo(1) and IsButtonHeld("attack") then
	-- Get weapon name from current state
	local stateName = GetCurrentStateName()
	if string.find(stateName, "machinegun") then
		SetNextState("s_machinegun_1")
	elseif string.find(stateName, "chaingun") then
		SetNextState("s_chaingun_1")
	end
end
