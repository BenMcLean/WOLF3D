using Godot;
using System;
using System.Linq;
using BenMcLean.Wolf3D.Shared;

namespace BenMcLean.Wolf3D.VR
{
	public partial class SetupRoom : Node3D
	{
		#region Data members
		// TODO: Load path should come from application configuration/settings
		public static string Load = null;
		private DosScreen DosScreen;
		private XROrigin3D XROrigin;
		private XRCamera3D XRCamera;
		private XRController3D LeftController;
		private XRController3D RightController;

		public enum LoadingState
		{
			READY,
			ASK_PERMISSION,
			GET_SHAREWARE,
			LOAD_ASSETS,
			EXCEPTION
		}

		private LoadingState state = LoadingState.READY;
		public LoadingState State
		{
			get => state;
			set
			{
				state = value;
				switch (State)
				{
					case LoadingState.ASK_PERMISSION:
						WriteLine("This application requires permission to both read and write to your device's")
							.WriteLine("external storage.")
							.WriteLine("Press any button to continue.");
						break;
					case LoadingState.GET_SHAREWARE:
						// TODO: Implement command line argument parsing
						// if (OS.GetCmdlineArgs()
						//     ?.Where(e => !e.StartsWith("-") && System.IO.File.Exists(e))
						//     ?.FirstOrDefault() is string load)
						//     Load = load;
						WriteLine("Installing Wolfenstein 3-D Shareware!");
						try
						{
							Shareware();
						}
						catch (Exception ex)
						{
							WriteLine(ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
							break;
						}
						State = LoadingState.LOAD_ASSETS;
						break;
					case LoadingState.LOAD_ASSETS:
						LoadAssets();
						break;
				}
			}
		}
		#endregion Data members

		#region Godot
		public SetupRoom()
		{
			Name = "SetupRoom";
			ProcessMode = ProcessModeEnum.Always;

			AddChild(XROrigin = new XROrigin3D());
			XROrigin.AddChild(XRCamera = new XRCamera3D()
			{
				Current = true,
			});
			XROrigin.AddChild(LeftController = new XRController3D()
			{
				Tracker = "left_hand",
			});
			XROrigin.AddChild(RightController = new XRController3D()
			{
				Tracker = "right_hand",
			});

			// Create DosScreen instance
			DosScreen = new DosScreen();

			// Create quad to display DosScreen in 3D space
			MeshInstance3D quad = new MeshInstance3D()
			{
				Mesh = new QuadMesh()
				{
					Size = new Vector2(2.4384f, 1.8288f),
				},
				MaterialOverride = new StandardMaterial3D()
				{
					AlbedoTexture = DosScreen.GetViewport().GetTexture(),
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					DisableReceiveShadows = true,
					DisableAmbientLight = true,
					CullMode = BaseMaterial3D.CullModeEnum.Back,
					Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
				},
				Position = Vector3.Forward * 3f,
			};
			XRCamera.AddChild(quad);

			WriteLine("Platform detected: " + OS.GetName());
			// TODO: Detect VR mode from XR interface
			// WriteLine("VR mode: " + Main.VR);
		}

		public override void _Ready()
		{
			// TODO: Initialize XR interface
			// if (XRServer.FindInterface("OpenXR") is XRInterface xrInterface)
			// {
			//     if (xrInterface.Initialize())
			//         GetViewport().UseXR = true;
			// }
		}

		public override void _Process(double delta)
		{
			if (State == LoadingState.READY)
				switch (OS.GetName())
				{
					case "Android":
						// TODO: Implement permission checking for Android
						// State = PermissionsGranted ? LoadingState.GET_SHAREWARE : LoadingState.ASK_PERMISSION;
						State = LoadingState.GET_SHAREWARE;
						break;
					default:
						State = LoadingState.GET_SHAREWARE;
						break;
				}
		}
		#endregion Godot

		#region Android
		// TODO: Implement Android permission checking
		// public static bool PermissionsGranted =>
		//     OS.GetGrantedPermissions() is string[] permissions &&
		//     permissions != null &&
		//     permissions.Contains("android.permission.READ_EXTERNAL_STORAGE", StringComparer.InvariantCultureIgnoreCase) &&
		//     permissions.Contains("android.permission.WRITE_EXTERNAL_STORAGE", StringComparer.InvariantCultureIgnoreCase);
		#endregion Android

		#region VR
		// TODO: Implement button press handling
		// public void ButtonPressed(int buttonIndex)
		// {
		//     if (IsVRButton(buttonIndex))
		//         switch (State)
		//         {
		//             case LoadingState.ASK_PERMISSION:
		//                 if (PermissionsGranted)
		//                     State = LoadingState.GET_SHAREWARE;
		//                 else
		//                     OS.RequestPermissions();
		//                 break;
		//         }
		// }

		public SetupRoom WriteLine(string message)
		{
			GD.Print(message);
			DosScreen.WriteLine(message);
			return this;
		}
		#endregion VR

		#region Shareware
		public void Shareware()
		{
			// TODO: Implement shareware installation
			// This would copy game XML files and extract shareware data
			// Reference old implementation for details
			WriteLine("Shareware installation not yet implemented");
		}

		// TODO: Implement file listing
		// public static System.Collections.Generic.IEnumerable<string> ListFiles(string path = null, string filter = "*.*")
		// {
		//     filter = WildCardToRegular(filter);
		//     DirAccess dir = DirAccess.Open(path ?? "res://");
		//     if (dir != null)
		//     {
		//         dir.ListDirBegin();
		//         string fileName;
		//         while ((fileName = dir.GetNext()) != "")
		//         {
		//             if (fileName[0] != '.' && System.Text.RegularExpressions.Regex.IsMatch(fileName, filter))
		//                 yield return fileName;
		//         }
		//         dir.ListDirEnd();
		//     }
		// }

		// public static string WildCardToRegular(string value) =>
		//     "^" + System.Text.RegularExpressions.Regex.Escape(value).Replace("\\?", ".").Replace("\\*", ".*") + "$";
		#endregion Shareware

		#region LoadAssets
		public void LoadAssets()
		{
			WriteLine(Load is string ? "Loading \"" + Load + "\"..." : "Loading game selection menu...");
			// TODO: Implement asset loading
			// This would load Wolf3D assets and transition to menu
			// AmbientTasks.Add(Task.Run(LoadAssets2));
		}

		// TODO: Implement async asset loading
		// public static void LoadAssets2()
		// {
		//     if (Load is string)
		//     {
		//         Main.Folder = System.IO.Path.GetDirectoryName(Load);
		//         Assets.Load(Main.Folder, System.IO.Path.GetFileName(Load));
		//         Settings.Load();
		//         Main.StatusBar = new StatusBar();
		//         Main.MenuRoom = new MenuRoom();
		//     }
		//     else
		//     {
		//         Assets.Load(
		//             Main.Folder = System.IO.Path.Combine(Main.Path, "WL1"),
		//             Assets.LoadXML(Main.Folder).InsertGameSelectionMenu(),
		//             true
		//         );
		//         Settings.Load();
		//         Main.MenuRoom = new MenuRoom("_GameSelect0");
		//     }
		//     Main.Room = Main.MenuRoom;
		// }
		#endregion LoadAssets
	}
}
