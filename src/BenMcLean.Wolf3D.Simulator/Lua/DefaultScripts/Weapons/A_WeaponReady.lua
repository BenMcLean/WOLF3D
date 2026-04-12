-- End of attack sequence - check if out of ammo
-- WL_AGENT.C case -1: switch to knife if no ammo
if not HasAmmo(1) then
	SwitchToLowestWeapon()
end
