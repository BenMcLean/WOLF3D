using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BenMcLean.Wolf3D.Assets.Gameplay;

namespace BenMcLean.Wolf3D.Shared.Menu;

/// <summary>
/// Builds a procedural MenuCollection for the game selection screen.
/// Scans the games/ directory for XML game definition files and creates
/// a selectable paged list using the shareware (WL1) visual assets already loaded
/// via SharedAssetManager.
/// "Have it list games." --Matthew Broderick as David Lightman in WarGames. (1983)
/// </summary>
public static class GameSelectionMenuFactory
{
	public const int PerPage = 10;

	/// <summary>
	/// Builds a MenuCollection containing paginated "_GameSelectN" menus that list
	/// all XML game definition files found in the specified directory.
	/// Each menu item calls SelectGame(path) when activated.
	/// WL1 is sorted first, then alphabetically by filename.
	/// </summary>
	/// <param name="gamesDirectory">Absolute path to the games/ directory</param>
	/// <returns>A MenuCollection ready to pass to MenuRoom as MenuCollectionOverride</returns>
	public static MenuCollection Build(string gamesDirectory)
	{
		string[] xmlFiles;
		try
		{
			xmlFiles = Directory.GetFiles(gamesDirectory, "*.xml");
		}
		catch
		{
			xmlFiles = [];
		}

		// WL1 sorts first, then alphabetically by filename (matching original game ordering)
		string[] games = [.. xmlFiles
			.OrderBy(f => !Path.GetFileNameWithoutExtension(f).Equals("WL1", StringComparison.OrdinalIgnoreCase))
			.ThenBy(f => Path.GetFileNameWithoutExtension(f))];

		int pages = games.Length == 0 ? 1 : (games.Length - 1) / PerPage + 1;

		MenuCollection collection = new()
		{
			StartMenu = "_GameSelect0",
			DefaultFont = "SMALL",
			DefaultSpacing = 13,
			DefaultTextColor = 0x17,          // WL_MENU.H:TEXTCOLOR
			DefaultHighlight = 0x13,          // WL_MENU.H:HIGHLIGHT
			DefaultBorderColor = 0x29,        // WL_MENU.H:BORDCOLOR
			DefaultBoxBackgroundColor = 0x2d, // WL_MENU.H:BKGDCOLOR
			DefaultDeactive = 0x2b,           // WL_MENU.H:DEACTIVE
			DefaultBorder2Color = 0x23,       // WL_MENU.H:BORD2COLOR
			DefaultCursorPic = "C_CURSOR1PIC",
			DefaultCursorMoveSound = "MOVEGUN2SND",
		};
		collection.EndStrings.Add("Are you sure you want\nto quit?");

		for (int page = 0; page < pages; page++)
		{
			int prevPage = (page - 1 + pages) % pages;
			int nextPage = (page + 1) % pages;

			MenuDefinition menu = new()
			{
				Name = "_GameSelect" + page,
				BorderColor = 0x29,
				TextColor = 0x17,
				Highlight = 0x13,
				Font = "SMALL",
				CursorPic = "C_CURSOR1PIC",
				CursorMoveSound = "MOVEGUN2SND",
				X = 8,
				Y = 22,
				Indent = 24,
				Spacing = 13,
				Texts =
				[
					new MenuTextDefinition
					{
						Content = "Which game to play?",
						X = "Center",
						Y = "3",
						Color = 71, // READHCOLOR - yellow
					},
				],
				Boxes =
				[
					new MenuBoxDefinition
					{
						X = 2,
						Y = 19,
						W = 316,
						H = 168,
						BackgroundColor = 45, // PixelRect Color
						Deactive = 44,        // PixelRect BordColor
						Border2Color = 35,    // PixelRect Bord2Color
					},
				],
			};

			if (pages > 1)
			{
				menu.Texts.Add(new MenuTextDefinition
				{
					Content = "pg " + (page + 1) + " of " + pages,
					X = "220",
					Y = "188",
					Color = 0x17,
				});

				menu.Items.Add(new MenuItemDefinition
				{
					Text = "\u00ab Prev",
					Script = $"NavigateToMenu(\"_GameSelect{prevPage}\")",
				});
			}

			if (games.Length == 0)
			{
				menu.Items.Add(new MenuItemDefinition
				{
					Text = "No games found",
					Script = "",
				});
			}
			else
			{
				int startIdx = page * PerPage;
				int endIdx = Math.Min(startIdx + PerPage, games.Length);
				for (int i = startIdx; i < endIdx; i++)
				{
					string name = ReadGameName(games[i]);
					string normalizedPath = games[i].Replace('\\', '/');
					menu.Items.Add(new MenuItemDefinition
					{
						Text = name,
						Script = $"SelectGame(\"{normalizedPath}\")",
					});
				}
			}

			if (pages > 1)
			{
				menu.Items.Add(new MenuItemDefinition
				{
					Text = "Next \u00bb",
					Script = $"NavigateToMenu(\"_GameSelect{nextPage}\")",
				});
			}

			collection.AddMenu(menu);
		}

		return collection;
	}

	/// <summary>
	/// Reads the Name attribute from the root Game element of an XML game definition file.
	/// Falls back to the filename (without extension) if the attribute is absent or the
	/// file cannot be read.
	/// </summary>
	private static string ReadGameName(string xmlPath)
	{
		try
		{
			return XDocument.Load(xmlPath).Root?.Attribute("Name")?.Value
				?? Path.GetFileNameWithoutExtension(xmlPath);
		}
		catch
		{
			return Path.GetFileNameWithoutExtension(xmlPath);
		}
	}
}
