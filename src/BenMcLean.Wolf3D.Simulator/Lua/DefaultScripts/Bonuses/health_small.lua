if GetValue("Health") < GetMax("Health") then
	AddValue("Health", 4)
	PlaySound(ResolveSound("HEALTH1SND", "D_BONUSSND"))
	FlashScreen(0xFFF800)
	return true
end
return false
