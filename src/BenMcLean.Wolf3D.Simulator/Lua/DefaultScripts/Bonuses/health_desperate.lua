if GetValue("Health") < 12 then
	AddValue("Health", 1)
	PlayDigiSound("SLURPIESND")
	FlashScreen(0xFFF800)
	return true
end
return false
