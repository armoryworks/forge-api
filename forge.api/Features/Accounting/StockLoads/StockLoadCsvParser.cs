using System.Globalization;

using CsvHelper;
using CsvHelper.Configuration;
using FluentValidation.Results;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>
/// Reads the opening-stock CSV template. Headers are case-insensitive and order-insensitive:
/// <c>partNumber, location, lot, expirationDate, quantity, unitCost</c> (<c>location</c>, <c>lot</c> and
/// <c>expirationDate</c> may be blank). Numbers use the invariant culture; dates are <c>yyyy-MM-dd</c> or
/// <c>MM/dd/yyyy</c>. A cell that does not parse is reported against its row as <c>lines[i].field</c>.
/// </summary>
internal static class StockLoadCsvParser
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy"];

    public static (List<StockLoadLineInput> Lines, List<ValidationFailure> Errors) Parse(string csvText)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
            MissingFieldFound = null,
            BadDataFound = null,
            HeaderValidated = null,
        };

        var lines = new List<StockLoadLineInput>();
        var errors = new List<ValidationFailure>();

        using var reader = new StringReader(csvText);
        using var csv = new CsvReader(reader, config);
        if (!csv.Read())
            return (lines, errors);
        csv.ReadHeader();

        while (csv.Read())
        {
            var partNumber = Get(csv, "partnumber");
            var location = Get(csv, "location");
            var lot = Get(csv, "lot") ?? Get(csv, "lotnumber");
            var expirationRaw = Get(csv, "expirationdate");
            var quantityRaw = Get(csv, "quantity");
            var unitCostRaw = Get(csv, "unitcost");
            if (partNumber is null && location is null && lot is null && quantityRaw is null && unitCostRaw is null)
                continue;

            var index = lines.Count;
            if (!decimal.TryParse(quantityRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity))
                errors.Add(Failure(index, "quantity", "invalid-number", $"Row {index + 1}: quantity '{quantityRaw}' is not a number."));
            if (!decimal.TryParse(unitCostRaw, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out var unitCost))
                errors.Add(Failure(index, "unitCost", "invalid-number", $"Row {index + 1}: unit cost '{unitCostRaw}' is not a number."));

            DateOnly? expiration = null;
            if (expirationRaw is not null)
            {
                if (DateOnly.TryParseExact(expirationRaw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    expiration = parsed;
                else
                    errors.Add(Failure(index, "expirationDate", "invalid-date", $"Row {index + 1}: expiration date '{expirationRaw}' is not a date."));
            }

            lines.Add(new StockLoadLineInput(partNumber, location, lot, expiration, quantity, unitCost));
        }

        return (lines, errors);
    }

    /// <summary>
    /// The rows of a create or update request: the CSV text when one was sent, else the JSON lines. A malformed
    /// cell throws a <see cref="FluentValidation.ValidationException"/> naming its row.
    /// </summary>
    public static IReadOnlyList<StockLoadLineInput> FromRequest(IReadOnlyList<StockLoadLineInput>? lines, string? csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
            return lines ?? [];

        var (parsed, errors) = Parse(csvContent);
        if (errors.Count > 0)
            throw new FluentValidation.ValidationException(errors);
        return parsed;
    }

    private static string? Get(CsvReader csv, string field)
        => csv.TryGetField<string>(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static ValidationFailure Failure(int index, string field, string code, string message)
        => new($"lines[{index}].{field}", message) { ErrorCode = code };
}
