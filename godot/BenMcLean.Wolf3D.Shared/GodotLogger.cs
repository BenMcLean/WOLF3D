using Godot;
using Microsoft.Extensions.Logging;
using System;

namespace BenMcLean.Wolf3D.Shared;

/// <summary>
/// ILogger implementation that routes log messages to Godot's GD.Print/GD.PrintErr.
/// </summary>
public class GodotLogger(string categoryName) : ILogger
{
	public IDisposable BeginScope<TState>(TState state) => null;
	public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
	public void Log<TState>(
		LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception exception,
		Func<TState, Exception, string> formatter)
	{
		if (!IsEnabled(logLevel))
			return;
		string message = formatter(state, exception),
			logMessage = $"[{logLevel}] {categoryName}: {message}";
		if (exception is not null)
			logMessage += $"\n{exception}";
		// Route to appropriate Godot logging function
		switch (logLevel)
		{
			case LogLevel.Critical:
			case LogLevel.Error:
				GD.PrintErr(logMessage);
				break;
			case LogLevel.Warning:
			default:
				GD.Print(logMessage);
				break;
		}
	}
	/// <summary>
	/// ILoggerProvider that creates GodotLogger instances.
	/// Routes Microsoft.Extensions.Logging to Godot's logging system.
	/// Use this in any Godot project to enable logging from non-Godot libraries.
	/// </summary>
	public class GodotLoggerProvider : ILoggerProvider
	{
		public ILogger CreateLogger(string categoryName) => new GodotLogger(categoryName);
		public void Dispose() { }
	}
}
