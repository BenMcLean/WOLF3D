if GetValue("Health") < GetMax("Health") then
	AddValue("Health", 10)
	PlayAdLibSound("HEALTH1SND")
	FlashScreen(0xFFF800)
	return true
end
return false
