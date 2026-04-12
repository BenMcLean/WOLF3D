if GetValue("Health") < GetMax("Health") then
	AddValue("Health", 25)
	PlayAdLibSound("HEALTH2SND")
	FlashScreen(0xFFF800)
	return true
end
return false
