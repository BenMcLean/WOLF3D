AddValue("Lives", 1)
SetValue("Health", GetMax("Health"))
AddValue("Ammo", 25)
PlayAdLibSound("BONUS1UPSND")
FlashScreen(0xFFF800)
return true
