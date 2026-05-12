-- Rapid-fire loop frame: optionally fires, then loops back to fire state if trigger held.
-- WL_AGENT.C:T_Attack case 3 (machine gun - loop only) and case 4 (chain gun - fire then loop).
-- Chain gun fires here because case 4 falls through to case 1 in the original switch statement.
local ammoType = GetWeaponProperty("AmmoType")
if ammoType and GetValue(ammoType) < 1 then return end
if GetWeaponType() == "chaingun" then
	PlaySound(ResolveSound(GetWeaponProperty("FireSound"), ""))
	if ammoType then AddValue(ammoType, -1) end
	RequestHitScan({
		maxRange = 100,
		spread = 0,
		damageBase = 5,
		damageRandom = 10,
		damageRangeModifier = true
	})
	PropagateNoise()
end
if (not ammoType or GetValue(ammoType) >= 1) and IsButtonHeld("attack") then
	SetNextState(GetWeaponProperty("FireState"))
end
