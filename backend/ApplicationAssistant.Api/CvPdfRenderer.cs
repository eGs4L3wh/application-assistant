using System.Globalization;
using ApplicationAssistant.AI;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ApplicationAssistant.Api;

public sealed class CvPdfRenderer
{
    static CvPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(
        string fullName,
        string email,
        string? phone,
        CvDraft draft,
        IReadOnlyList<CvDraftExperience> experiences,
        IReadOnlyList<CvDraftEducation> education)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginVertical(36);
                page.MarginHorizontal(42);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3).LineHeight(1.25f));
                page.PageColor(Colors.White);

                page.Header().Element(header =>
                {
                    header.Column(col =>
                    {
                        col.Item().Text(fullName)
                            .FontSize(20)
                            .SemiBold()
                            .FontColor(Colors.BlueGrey.Darken4);

                        var contactParts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            contactParts.Add(email.Trim());
                        }

                        if (!string.IsNullOrWhiteSpace(phone))
                        {
                            contactParts.Add(phone.Trim());
                        }

                        if (contactParts.Count > 0)
                        {
                            col.Item().PaddingTop(2).Text(string.Join("  ·  ", contactParts))
                                .FontSize(9)
                                .FontColor(Colors.Grey.Darken1);
                        }

                        col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.BlueGrey.Lighten2);
                    });
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Spacing(10);

                    if (!string.IsNullOrWhiteSpace(draft.Summary))
                    {
                        col.Item().Element(e => SectionTitle(e, "Summary"));
                        col.Item().Text(draft.Summary).FontSize(10);
                    }

                    if (experiences.Count > 0)
                    {
                        col.Item().Element(e => SectionTitle(e, "Experience"));
                        foreach (var exp in experiences)
                        {
                            col.Item().Element(item => RenderExperience(item, exp));
                        }
                    }

                    if (education.Count > 0)
                    {
                        col.Item().Element(e => SectionTitle(e, "Education"));
                        foreach (var edu in education)
                        {
                            col.Item().Element(item => RenderEducation(item, edu));
                        }
                    }

                    if (draft.Skills.Count > 0)
                    {
                        col.Item().Element(e => SectionTitle(e, "Skills"));
                        col.Item().Text(string.Join(" · ", draft.Skills)).FontSize(9.5f);
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void SectionTitle(IContainer container, string title)
    {
        container.PaddingBottom(2).Text(title.ToUpperInvariant())
            .FontSize(11)
            .SemiBold()
            .LetterSpacing(0.04f)
            .FontColor(Colors.BlueGrey.Darken3);
    }

    private static void RenderExperience(IContainer container, CvDraftExperience exp)
    {
        container.PaddingBottom(6).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span(exp.Title).SemiBold().FontSize(10.5f);
                    if (!string.IsNullOrWhiteSpace(exp.Company))
                    {
                        text.Span($"  ·  {exp.Company}").FontSize(10);
                    }
                });
                row.ConstantItem(110).AlignRight().Text(FormatRange(exp.StartDate, exp.EndDate, exp.IsCurrent))
                    .FontSize(8.5f)
                    .FontColor(Colors.Grey.Darken1);
            });

            if (!string.IsNullOrWhiteSpace(exp.Location))
            {
                col.Item().Text(exp.Location!).FontSize(8.5f).FontColor(Colors.Grey.Darken1);
            }

            if (!string.IsNullOrWhiteSpace(exp.EngagementType))
            {
                col.Item().Text(exp.EngagementType).FontSize(8.5f).FontColor(Colors.Grey.Darken1);
            }

            if (exp.Skills is { Count: > 0 })
            {
                col.Item().PaddingTop(1).Text(string.Join(" · ", exp.Skills))
                    .FontSize(8.5f)
                    .FontColor(Colors.BlueGrey.Darken2);
            }

            if (!string.IsNullOrWhiteSpace(exp.Description))
            {
                col.Item().PaddingTop(2).Text(exp.Description!).FontSize(9.5f);
            }
        });
    }

    private static void RenderEducation(IContainer container, CvDraftEducation draft)
    {
        container.PaddingBottom(4).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span(draft.Institution).SemiBold().FontSize(10.5f);
                    var degreeBits = new[] { draft.Degree, draft.FieldOfStudy }
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    var degree = string.Join(", ", degreeBits);
                    if (!string.IsNullOrWhiteSpace(degree))
                    {
                        text.Span($"  ·  {degree}").FontSize(10);
                    }
                });
                row.ConstantItem(110).AlignRight().Text(FormatRange(draft.StartDate, draft.EndDate, isCurrent: false))
                    .FontSize(8.5f)
                    .FontColor(Colors.Grey.Darken1);
            });

            if (!string.IsNullOrWhiteSpace(draft.Description))
            {
                col.Item().Text(draft.Description!).FontSize(9.5f);
            }
        });
    }

    private static string FormatRange(DateTime? start, DateTime? end, bool isCurrent)
    {
        var from = FormatMonth(start);
        var to = isCurrent || end is null ? "Present" : FormatMonth(end);
        if (from == "—" && to == "—") return "";
        return $"{from} – {to}";
    }

    private static string FormatMonth(DateTime? value) =>
        value?.ToString("MMM yyyy", CultureInfo.InvariantCulture) ?? "—";
}
