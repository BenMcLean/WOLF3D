namespace BenMcLean.Wolf3D.Assets.Gameplay;

/// <summary>
/// Dynamic Bible quiz payload shared between gameplay and menu systems.
/// Populated when a quiz scroll is consumed, then read by the Quiz menu.
/// </summary>
/// <param name="QuestionIndex">Zero-based question number in the source bank.</param>
/// <param name="QuestionText">Question text to display.</param>
/// <param name="ReferenceText">Bible reference shown on easier difficulties.</param>
/// <param name="Answers">Answer labels in display order.</param>
/// <param name="CorrectAnswerIndex">Zero-based index of the correct answer.</param>
/// <param name="ShowReference">True when the menu should show the Bible reference.</param>
public sealed record PendingQuizData(
	int QuestionIndex,
	string QuestionText,
	string ReferenceText,
	string[] Answers,
	int CorrectAnswerIndex,
	bool ShowReference);
