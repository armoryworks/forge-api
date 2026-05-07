using MediatR;
using Microsoft.EntityFrameworkCore;

using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.Reports;

public record ExportReportQuery(int ReportId, ReportExportFormat Format) : IRequest<ExportReportResult>;

public record ExportReportResult(byte[] Content, string ContentType, string FileName);

public class ExportReportHandler(
    AppDbContext db,
    IMediator mediator,
    IClock clock) : IRequestHandler<ExportReportQuery, ExportReportResult>
{
    public async Task<ExportReportResult> Handle(ExportReportQuery request, CancellationToken cancellationToken)
    {
        var report = await db.SavedReports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.ReportId, cancellationToken)
            ?? throw new KeyNotFoundException($"Saved report {request.ReportId} not found.");

        var runResult = await mediator.Send(new RunReportCommand(
            report.EntitySource,
            System.Text.Json.JsonSerializer.Deserialize<string[]>(report.ColumnsJson) ?? [],
            null,
            report.GroupByField,
            report.SortField,
            report.SortDirection,
            1,
            10000), cancellationToken);

        return request.Format switch
        {
            ReportExportFormat.Csv => ExportToCsv(runResult, report.Name),
            ReportExportFormat.Xlsx => ExportToXlsx(runResult, report.Name),
            ReportExportFormat.Pdf => ExportToPdf(runResult, report.Name, clock.UtcNow),
            _ => throw new InvalidOperationException($"Unsupported export format: {request.Format}"),
        };
    }

    private static ExportReportResult ExportToCsv(RunReportResponseModel result, string reportName)
    {
        var sb = new System.Text.StringBuilder();

        if (result.Rows.Count > 0)
        {
            var headers = result.Rows[0].Keys;
            sb.AppendLine(string.Join(",", headers.Select(h => $"\"{h}\"")));

            foreach (var row in result.Rows)
            {
                var values = headers.Select(h => row.TryGetValue(h, out var v) ? $"\"{v?.ToString()?.Replace("\"", "\"\"")}\"" : "\"\"");
                sb.AppendLine(string.Join(",", values));
            }
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return new ExportReportResult(bytes, "text/csv", $"{reportName}.csv");
    }

    private static ExportReportResult ExportToXlsx(RunReportResponseModel result, string reportName)
    {
        using var workbook = new XLWorkbook();
        var sheetName = reportName.Length > 31 ? reportName[..31] : reportName;
        var worksheet = workbook.Worksheets.Add(sheetName);

        if (result.Rows.Count > 0)
        {
            var headers = result.Columns;

            for (var c = 0; c < headers.Length; c++)
            {
                var cell = worksheet.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0, 120, 120);
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var r = 0; r < result.Rows.Count; r++)
            {
                var row = result.Rows[r];
                for (var c = 0; c < headers.Length; c++)
                {
                    var value = row.TryGetValue(headers[c], out var v) ? v : null;
                    var cell = worksheet.Cell(r + 2, c + 1);

                    if (value is null)
                        cell.Value = "";
                    else if (value is int intVal)
                        cell.Value = intVal;
                    else if (value is long longVal)
                        cell.Value = longVal;
                    else if (value is decimal decVal)
                        cell.Value = (double)decVal;
                    else if (value is double dblVal)
                        cell.Value = dblVal;
                    else if (value is bool boolVal)
                        cell.Value = boolVal;
                    else if (value is DateTime dtVal)
                    {
                        cell.Value = dtVal;
                        cell.Style.DateFormat.Format = "yyyy-MM-dd";
                    }
                    else if (value is DateTimeOffset dtoVal)
                    {
                        cell.Value = dtoVal.DateTime;
                        cell.Style.DateFormat.Format = "yyyy-MM-dd";
                    }
                    else
                        cell.Value = value.ToString();
                }
            }

            worksheet.Columns().AdjustToContents(1, 100);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new ExportReportResult(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{reportName}.xlsx");
    }

    private static ExportReportResult ExportToPdf(RunReportResponseModel result, string reportName, DateTimeOffset generatedAt)
    {
        var document = new ReportPdfDocument(result, reportName, generatedAt);
        return new ExportReportResult(
            document.GeneratePdf(),
            "application/pdf",
            $"{reportName}.pdf");
    }
}
