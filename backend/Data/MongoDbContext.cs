using ApplicationAssistant.Api.Models;
using MongoDB.Driver;

namespace ApplicationAssistant.Api.Data;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Mongo")
            ?? configuration["Mongo:ConnectionString"]
            ?? "mongodb://localhost:27017";
        var databaseName = configuration["Mongo:DatabaseName"] ?? "application-assistant";

        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(databaseName);
    }

    public IMongoCollection<AppUser> Users => _database.GetCollection<AppUser>("users");
    public IMongoCollection<CvDocument> Cvs => _database.GetCollection<CvDocument>("cvs");
    public IMongoCollection<JobApplication> Applications =>
        _database.GetCollection<JobApplication>("applications");

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        await Users.Indexes.CreateOneAsync(
            new CreateIndexModel<AppUser>(
                Builders<AppUser>.IndexKeys.Ascending(u => u.GoogleId),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);

        await Users.Indexes.CreateOneAsync(
            new CreateIndexModel<AppUser>(
                Builders<AppUser>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);

        await Cvs.Indexes.CreateOneAsync(
            new CreateIndexModel<CvDocument>(
                Builders<CvDocument>.IndexKeys
                    .Ascending(c => c.UserId)
                    .Descending(c => c.UploadedAt)),
            cancellationToken: ct);

        await Applications.Indexes.CreateOneAsync(
            new CreateIndexModel<JobApplication>(
                Builders<JobApplication>.IndexKeys
                    .Ascending(a => a.UserId)
                    .Descending(a => a.UpdatedAt)),
            cancellationToken: ct);
    }
}
