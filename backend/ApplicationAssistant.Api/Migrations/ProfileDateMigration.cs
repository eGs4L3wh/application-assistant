using ApplicationAssistant.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ApplicationAssistant.Api;

public static class ProfileDateMigration
{
    public static async Task<MigrationResult> RunAsync(IMongoDatabase database, CancellationToken ct = default)
    {
        var users = database.GetCollection<BsonDocument>("users");
        var cursor = await users.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct);

        var usersTouched = 0;
        var fieldsConverted = 0;
        var fieldsFailed = 0;

        foreach (var user in cursor)
        {
            var changed = false;

            if (user.TryGetValue("Experience", out var experienceValue) && experienceValue.IsBsonArray)
            {
                foreach (var item in experienceValue.AsBsonArray.Where(v => v.IsBsonDocument).Select(v => v.AsBsonDocument))
                {
                    changed |= ConvertField(item, "StartDate", ref fieldsConverted, ref fieldsFailed);
                    changed |= ConvertField(item, "EndDate", ref fieldsConverted, ref fieldsFailed);
                }
            }

            if (user.TryGetValue("Education", out var educationValue) && educationValue.IsBsonArray)
            {
                foreach (var item in educationValue.AsBsonArray.Where(v => v.IsBsonDocument).Select(v => v.AsBsonDocument))
                {
                    changed |= ConvertField(item, "StartDate", ref fieldsConverted, ref fieldsFailed);
                    changed |= ConvertField(item, "EndDate", ref fieldsConverted, ref fieldsFailed);
                }
            }

            if (!changed)
            {
                continue;
            }

            await users.ReplaceOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", user["_id"]),
                user,
                cancellationToken: ct);
            usersTouched++;
        }

        return new MigrationResult(usersTouched, fieldsConverted, fieldsFailed);
    }

    private static bool ConvertField(
        BsonDocument item,
        string fieldName,
        ref int fieldsConverted,
        ref int fieldsFailed)
    {
        if (!item.TryGetValue(fieldName, out var value) || value.IsBsonNull)
        {
            return false;
        }

        if (value.IsValidDateTime || value.BsonType == BsonType.DateTime)
        {
            return false;
        }

        if (!value.IsString)
        {
            fieldsFailed++;
            Console.WriteLine($"Skipping non-string {fieldName}: {value.BsonType}");
            return false;
        }

        var raw = value.AsString;
        if (CvDateParser.IsPresent(raw))
        {
            item[fieldName] = BsonNull.Value;
            if (item.Contains("IsCurrent"))
            {
                item["IsCurrent"] = true;
            }

            fieldsConverted++;
            Console.WriteLine($"  {fieldName}: \"{raw}\" → null (present)");
            return true;
        }

        var parsed = CvDateParser.Parse(raw);
        if (parsed is null)
        {
            fieldsFailed++;
            Console.WriteLine($"  FAILED {fieldName}: \"{raw}\"");
            return false;
        }

        item[fieldName] = parsed.Value;
        fieldsConverted++;
        Console.WriteLine($"  {fieldName}: \"{raw}\" → {parsed:yyyy-MM-dd}");
        return true;
    }
}

public readonly record struct MigrationResult(int UsersTouched, int FieldsConverted, int FieldsFailed);
