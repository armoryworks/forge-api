using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Quality;

public record GetPpapLevelRequirementsQuery(int Level) : IRequest<List<PpapLevelRequirementResponseModel>>;

public class GetPpapLevelRequirementsHandler
    : IRequestHandler<GetPpapLevelRequirementsQuery, List<PpapLevelRequirementResponseModel>>
{
    private static readonly (string Name, string[] Requirements)[] Elements =
    [
        ("Design Records",                     ["R", "S", "S", "*", "R"]),
        ("Engineering Change Documents",       ["R", "S", "S", "*", "R"]),
        ("Customer Engineering Approval",      ["R", "R", "S", "*", "R"]),
        ("Design FMEA",                        ["R", "R", "S", "*", "R"]),
        ("Process Flow Diagrams",              ["R", "R", "S", "*", "R"]),
        ("Process FMEA",                       ["R", "R", "S", "*", "R"]),
        ("Control Plan",                       ["R", "R", "S", "*", "R"]),
        ("Measurement System Analysis",        ["R", "R", "S", "*", "R"]),
        ("Dimensional Results",                ["R", "S", "S", "*", "R"]),
        ("Material / Performance Test Results", ["R", "S", "S", "*", "R"]),
        ("Initial Process Studies (SPC)",      ["R", "R", "S", "*", "R"]),
        ("Qualified Laboratory Documentation", ["R", "S", "S", "*", "R"]),
        ("Appearance Approval Report",         ["S", "S", "S", "*", "R"]),
        ("Sample Production Parts",            ["R", "S", "S", "*", "R"]),
        ("Master Sample",                      ["R", "R", "S", "*", "R"]),
        ("Checking Aids",                      ["R", "R", "S", "*", "R"]),
        ("Customer-Specific Requirements",     ["R", "R", "S", "*", "R"]),
        ("Part Submission Warrant (PSW)",      ["R", "R", "R", "R", "R"]),
    ];

    public Task<List<PpapLevelRequirementResponseModel>> Handle(
        GetPpapLevelRequirementsQuery request, CancellationToken cancellationToken)
    {
        if (request.Level < 1 || request.Level > 5)
            throw new ArgumentOutOfRangeException(nameof(request.Level), "PPAP level must be between 1 and 5");

        var levelIndex = request.Level - 1;
        var result = new List<PpapLevelRequirementResponseModel>();

        for (var i = 0; i < Elements.Length; i++)
        {
            var req = Elements[i].Requirements[levelIndex];
            result.Add(new PpapLevelRequirementResponseModel
            {
                ElementNumber = i + 1,
                ElementName = Elements[i].Name,
                Requirement = req switch
                {
                    "R" => "Retain",
                    "S" => "Submit",
                    "*" => "Submit + Retain",
                    _ => req,
                },
                IsRequired = true,
            });
        }

        return Task.FromResult(result);
    }
}
