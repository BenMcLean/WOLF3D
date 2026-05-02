using BenMcLean.Wolf3D.GameChecker;

if (args.Length == 0)
{
	Console.Error.WriteLine("Usage: GameChecker <game-xml-file>");
	return 1;
}

string xmlPath = args[0];
if (!File.Exists(xmlPath))
{
	Console.Error.WriteLine($"File not found: {xmlPath}");
	return 1;
}

List<GameCheckerLogic.Issue> issues = GameCheckerLogic.Check(xmlPath).ToList();

if (issues.Count == 0)
{
	Console.WriteLine($"OK: {Path.GetFileName(xmlPath)} validated successfully.");
	return 0;
}

foreach (GameCheckerLogic.Issue issue in issues)
	Console.WriteLine($"({issue.Line},{issue.Column}): {issue.Context}: {issue.Message}");

Console.WriteLine($"\n{issues.Count} issue(s)");
return 1;
