-- WL_AGENT.C:KnifeAttack (line 1862)
-- Knife attack - melee range attack
PlaySound(ResolveSound("ATKKNIFESND", "D_KNIFESND"))
RequestMelee({
	range = 1.5,
	arc = 60,
	damageBase = 0,
	damageRandom = 16 -- US_RndT() >> 4
})
