AddValue("Lives", 1)
SetValue("Health", GetMax("Health"))
AddValue("bullets", 25)
PlaySound("BONUS1UPSND")
FlashScreen(0xFFF800)
return true
