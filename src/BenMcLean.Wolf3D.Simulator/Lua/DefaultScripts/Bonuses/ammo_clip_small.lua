if GetValue("bullets") < GetMax("bullets") then
	AddValue("bullets", 4)
	PlaySound(ResolveSound("GETAMMOSND", "D_BONUSSND"))
	FlashScreen(0xFFF800)
	return true
end
return false
