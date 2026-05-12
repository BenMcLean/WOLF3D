-- Machine gun attack - single hitscan shot
local ammoType = GetWeaponProperty("AmmoType")
if ammoType and GetValue(ammoType) < 1 then return end
PlaySound(ResolveSound("ATKMACHINEGUNSND", "D_GATLINSND"))
if ammoType then AddValue(ammoType, -1) end
RequestHitScan({
	maxRange = 100,
	spread = 0,
	damageBase = 5,
	damageRandom = 10,
	damageRangeModifier = true
})
PropagateNoise()
