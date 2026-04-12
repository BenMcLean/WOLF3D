-- Pistol attack - single hitscan shot
if not HasAmmo(1) then return end
PlaySound("ATKPISTOLSND")
ConsumeAmmo(1)
RequestHitScan({
	maxRange = 100,
	spread = 0,
	damageBase = 5,
	damageRandom = 10,
	damageRangeModifier = true
})
