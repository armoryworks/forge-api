using Microsoft.Extensions.Logging;

using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;

using Forge.Core.Interfaces;

namespace Forge.Integrations;

/// <summary>
/// Fills AcroForm fields in government PDFs using PDFsharp 6.x (MIT).
/// </summary>
public class PdfSharpFormFillService(ILogger<PdfSharpFormFillService> logger) : IPdfFormFillService
{
    public Task<byte[]> FillFormAsync(
        byte[] templateBytes,
        Dictionary<string, string> fieldValues,
        bool flatten,
        CancellationToken ct)
    {
        using var inputStream = new MemoryStream(templateBytes);
        using var document = PdfReader.Open(inputStream, PdfDocumentOpenMode.Modify);

        var form = document.AcroForm;
        if (form is null)
        {
            logger.LogWarning("PDF has no AcroForm — returning original bytes unchanged.");
            return Task.FromResult(templateBytes);
        }

        var filledCount = 0;
        foreach (var (fieldName, value) in fieldValues)
        {
            var field = form.Fields[fieldName];
            if (field is null)
            {
                logger.LogDebug("AcroForm field '{FieldName}' not found in PDF — skipping.", fieldName);
                continue;
            }

            SetFieldValue(field, value);
            filledCount++;
        }

        logger.LogInformation("Filled {Count}/{Total} AcroForm fields.", filledCount, fieldValues.Count);

        if (flatten)
        {
            // Mark all fields read-only (bit 0 of Ff flag)
            for (var i = 0; i < form.Fields.Count; i++)
            {
                var field = form.Fields[i];
                if (field is null) continue;
                var ff = field.Elements.GetInteger("/Ff");
                field.Elements.SetInteger("/Ff", ff | 1);
            }
        }

        using var outputStream = new MemoryStream();
        document.Save(outputStream, closeStream: false);
        return Task.FromResult(outputStream.ToArray());
    }

    public Task<IReadOnlyList<string>> GetFieldNamesAsync(byte[] pdfBytes, CancellationToken ct)
    {
        using var inputStream = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(inputStream, PdfDocumentOpenMode.Import);

        var form = document.AcroForm;
        if (form is null)
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var names = new List<string>();
        for (var i = 0; i < form.Fields.Count; i++)
        {
            var field = form.Fields[i];
            if (field is not null && !string.IsNullOrWhiteSpace(field.Name))
                names.Add(field.Name);
        }

        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    private static void SetFieldValue(PdfAcroField field, string value)
    {
        switch (field)
        {
            case PdfTextField textField:
                textField.Value = new PdfString(value);
                break;

            case PdfCheckBoxField checkBox:
                checkBox.Checked = value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || value == "1"
                    || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("x", StringComparison.OrdinalIgnoreCase);
                break;

            case PdfRadioButtonField radioGroup:
                radioGroup.Value = new PdfName("/" + value.TrimStart('/'));
                break;

            case PdfComboBoxField comboBox:
                comboBox.Value = new PdfString(value);
                break;

            case PdfListBoxField listBox:
                listBox.Value = new PdfString(value);
                break;
        }
    }
}
