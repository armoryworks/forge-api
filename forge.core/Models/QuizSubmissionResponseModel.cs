namespace Forge.Core.Models;

public record QuizScoredQuestionModel(
    string QuestionId,
    bool IsCorrect,
    string CorrectOptionId,
    string? Explanation
);

public record QuizSubmissionResponseModel(
    int Score,
    bool Passed,
    QuizScoredQuestionModel[] Questions
);
