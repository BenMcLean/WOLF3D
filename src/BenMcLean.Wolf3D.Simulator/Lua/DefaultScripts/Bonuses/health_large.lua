if GetValue("Health") < GetMax("Health") then
	AddValue("Health", 25)
	PlaySound(ResolveSound("HEALTH2SND", "D_BONUSSND"))
	FlashScreen(0xFFF800)
	return true
end
return false
