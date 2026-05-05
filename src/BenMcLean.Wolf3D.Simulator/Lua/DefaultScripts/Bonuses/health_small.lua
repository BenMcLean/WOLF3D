if GetValue("Health") < GetMax("Health") then
	AddValue("Health", 4)
	PlaySound("HEALTH1SND")
	FlashScreen(0xFFF800)
	return true
end
return false
