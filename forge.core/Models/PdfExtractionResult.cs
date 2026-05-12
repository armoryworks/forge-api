namespace Forge.Core.Models;

/// <summary>
/// Raw extraction result from pdf.js — text items and form field annotations per page.
/// </summary>
public record PdfExtractionResult(
    int PageCount,
    List<PdfPageExtraction> Pages);

public record PdfPageExtraction(
    int PageNumber,
    double Width,
    double Height,
    List<PdfTextItem> TextItems,
    List<PdfAnnotationItem> Annotations);

public record PdfTextItem(
    string Text,
    double X,
    double Y,
    double Width,
    double Height,
    string FontName,
    double FontSize,
    bool IsBold,
    string? Color);

public record PdfAnnotationItem(
    string Id,
    string FieldType,
    double X,
    double Y,
    double Width,
    double Height,
    string? FieldName,
    string? AlternativeText,
    string? DefaultValue,
    int? MaxLength,
    bool Required,
    bool ReadOnly,
    List<PdfAnnotationOption>? Options,
    string? RadioGroupName);

public record PdfAnnotationOption(
    string Value,
    string Label);
