using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Handles Android runtime permission checks that cannot be satisfied via a normal
/// permission dialog. MANAGE_EXTERNAL_STORAGE (All Files Access) requires the user
/// to grant it manually in system settings; this class detects and redirects.
/// All methods are no-ops on non-Android platforms.
/// </summary>
internal static class AndroidPermissions
{
	/// <summary>
	/// Returns true if the app has been granted All Files Access (MANAGE_EXTERNAL_STORAGE).
	/// Always returns true on non-Android platforms.
	/// </summary>
	internal static bool HasAllFilesAccess()
	{
		if (!OS.HasFeature("android"))
			return true;
		GodotObject jcw = Engine.GetSingleton("JavaClassWrapper");
		using GodotObject environment = (GodotObject)jcw.Call("wrap", "android.os.Environment");
		return (bool)environment.Call("isExternalStorageManager");
	}

	/// <summary>
	/// Launches the Android All Files Access settings page for this app.
	/// MANAGE_EXTERNAL_STORAGE cannot be granted via a normal permission dialog — the user
	/// must grant it manually in system settings. This method opens that page directly.
	/// Requires Android 11+ (API 30+). No-op on non-Android platforms.
	/// </summary>
	internal static void OpenAllFilesAccessSettings()
	{
		if (!OS.HasFeature("android"))
			return;
		GodotObject jcw = Engine.GetSingleton("JavaClassWrapper");
		// Get the application context via ActivityThread so we have a Context to call
		// startActivity on. startActivity from a non-Activity context requires FLAG_ACTIVITY_NEW_TASK.
		using GodotObject activityThread = (GodotObject)jcw.Call("wrap", "android.app.ActivityThread");
		using GodotObject app = (GodotObject)activityThread.Call("currentApplication");
		string packageName = (string)app.Call("getPackageName");
		// Use Intent.parseUri() — a static factory method — to construct the Intent.
		// This avoids calling a Java constructor directly, which JavaClassWrapper does not support.
		using GodotObject intentClass = (GodotObject)jcw.Call("wrap", "android.content.Intent");
		using GodotObject intent = (GodotObject)intentClass.Call("parseUri",
			"intent:#Intent;action=android.settings.MANAGE_APP_ALL_FILES_ACCESS_PERMISSION;end", 0);
		// Set the package URI as intent data separately to avoid encoding issues in the URI string.
		using GodotObject uriClass = (GodotObject)jcw.Call("wrap", "android.net.Uri");
		using GodotObject packageUri = (GodotObject)uriClass.Call("parse", "package:" + packageName);
		intent.Call("setData", packageUri);
		// FLAG_ACTIVITY_NEW_TASK = 0x10000000 — required when starting from a non-Activity context
		intent.Call("addFlags", 268435456);
		app.Call("startActivity", intent);
	}
}
