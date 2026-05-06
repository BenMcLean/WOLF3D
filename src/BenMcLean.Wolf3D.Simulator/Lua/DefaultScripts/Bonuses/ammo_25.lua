if GetValue("bullets") < GetMax("bullets") then
	AddValue("bullets", 25)
	PlaySound("GETAMMOBOXSND")
	FlashScreen(0xFFF800)
	return true
end
return false
