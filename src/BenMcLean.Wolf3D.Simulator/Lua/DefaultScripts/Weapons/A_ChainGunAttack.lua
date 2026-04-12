-- Chain gun attack - single hitscan shot (can continue without ammo)
if not HasAmmo(1) then return end
PlaySound("ATKGATLINGSND")
ConsumeAmmo(1)
RequestHitScan({
	maxRange = 100,
	spread = 0,
	damageBase = 5,
	damageRandom = 10,
	damageRangeModifier = true
})
