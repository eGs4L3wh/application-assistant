using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace ApplicationAssistant.AI;

public sealed class CvParser(ILLMService llm) : ICvParser
{
    private const string SystemPrompt = """
        You extract structured career data from CVs.
        Extract contact email and phone when they appear on the CV (header/footer or contact section). Leave them empty if absent or unclear.
        Also return job experience and education records that appear in the document.
        Use empty strings for unknown fields. Prefer concise descriptions.
        Dates may be free-form strings as written on the CV (e.g. "Jan 2020", "2018", "Present").
        For each experience, set engagementType to "Permanent" or "Contract" when the CV indicates it (e.g. contractor, freelance, fixed-term, permanent employee). Leave engagementType empty when unclear.
        For each experience, extract skills[] as short tags evidenced in that role (programming languages, frameworks, cloud providers, databases, tools, methods). Prefer concrete names (e.g. "C#", "AWS", "Kubernetes"). Leave skills empty when none are clear.
        """;

    public async Task<CvParseResult> ParseAsync(
        byte[] content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var cvText = ExtractText(content, contentType, fileName);
        if (string.IsNullOrWhiteSpace(cvText))
        {
            throw new InvalidOperationException("Could not extract any text from the uploaded CV.");
        }

        var sample = new CvParseResult
        {
            Email = "candidate@example.com",
            Phone = "+44 7700 900123",
            Experiences =
            [
                new ParsedWorkExperience
                {
                    Company = "Example Corp",
                    Title = "Software Engineer",
                    Location = "London",
                    StartDate = "Jan 2020",
                    EndDate = "Dec 2022",
                    IsCurrent = false,
                    EngagementType = "Permanent",
                    Description = "Built APIs and web apps.",
                    Skills = ["C#", "ASP.NET", "Azure"]
                }
            ],
            Education =
            [
                new ParsedEducation
                {
                    Institution = "Example University",
                    Degree = "BSc",
                    FieldOfStudy = "Computer Science",
                    StartDate = "2016",
                    EndDate = "2019",
                    Description = null
                }
            ]
        };

        var history = new[]
        {
            new InputItem
            {
                FromUser = true,
                Text = $"CV file name: {fileName}\n\nCV text:\n{cvText}"
            }
        };

        var parsed = await llm.GetResponseAsync(
            sample,
            SystemPrompt,
            history,
            temperature: 0.1m);

        parsed.Experiences = parsed.Experiences
            .Where(e => !string.IsNullOrWhiteSpace(e.Company) || !string.IsNullOrWhiteSpace(e.Title))
            .Select(e =>
            {
                e.Skills = NormalizeSkillTags(e.Skills);
                return e;
            })
            .ToList();
        parsed.Education = parsed.Education
            .Where(e => !string.IsNullOrWhiteSpace(e.Institution))
            .ToList();
        parsed.Email = string.IsNullOrWhiteSpace(parsed.Email) ? null : parsed.Email.Trim();
        parsed.Phone = string.IsNullOrWhiteSpace(parsed.Phone) ? null : parsed.Phone.Trim();

        return parsed;
    }

    internal static List<string> NormalizeSkillTags(IEnumerable<string>? skills) =>
        (skills ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

    private static string ExtractText(byte[] content, string contentType, string fileName)
    {
        var mime = contentType?.Trim() ?? string.Empty;
        var name = fileName ?? string.Empty;

        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || mime.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractPdf(content);
        }

        if (name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
            || mime.Equals(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                StringComparison.OrdinalIgnoreCase))
        {
            return ExtractDocx(content);
        }

        if (name.EndsWith(".doc", StringComparison.OrdinalIgnoreCase)
            || mime.Equals("application/msword", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Legacy .doc files are not supported for parsing. Please upload a PDF or .docx.");
        }

        // Last resort: treat as UTF-8 text
        return Encoding.UTF8.GetString(content);
    }

    private static string ExtractPdf(byte[] content)
    {
        using var document = PdfDocument.Open(content);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        return sb.ToString().Trim();
    }

    private static string ExtractDocx(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var text in body.Descendants<Text>())
        {
            sb.Append(text.Text);
            sb.Append(' ');
        }

        return sb.ToString().Trim();
    }
}
