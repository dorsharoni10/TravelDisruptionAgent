using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;

namespace TravelDisruptionAgent.Infrastructure.Services;

/// <summary>
/// Persists session messages in MongoDB (survives API restarts). Trimming matches in-memory limits.
/// Appends use a single aggregation-pipeline update (atomic) so concurrent writes do not drop messages.
/// </summary>
public sealed class MongoConversationSessionStore : IConversationSessionStore
{
    private readonly IMongoCollection<SessionDocument> _collection;
    private readonly ConversationSessionOptions _options;

    public MongoConversationSessionStore(
        IMongoClient client,
        IOptions<MongoDbOptions> mongoOptions,
        IOptions<ConversationSessionOptions> sessionOptions)
    {
        var mongo = mongoOptions.Value;
        var db = client.GetDatabase(mongo.DatabaseName);
        _collection = db.GetCollection<SessionDocument>(mongo.SessionsCollection);
        _options = sessionOptions.Value;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var doc = await _collection.Find(x => x.Id == sessionKey).FirstOrDefaultAsync(cancellationToken);
        if (doc?.Messages is null || doc.Messages.Count == 0)
            return [];

        return doc.Messages
            .Select(m => new ChatMessage(m.Role, m.Content, m.TimestampUtc))
            .ToList();
    }

    public Task AppendMessageAsync(string sessionKey, ChatMessage message, CancellationToken cancellationToken = default)
    {
        var stored = new StoredMessage
        {
            Role = message.Role,
            Content = message.Content,
            TimestampUtc = message.TimestampUtc
        };

        var maxMessages = Math.Max(1, _options.MaxMessages);
        var maxTotal = Math.Max(0, _options.MaxTotalStoredChars);
        // Upper bound for $reduce iterations: drop at most one message per iteration.
        var dropCap = Math.Max(256, maxMessages * 4);

        var newBson = StoredMessageToBson(stored);
        var pipeline = new[]
        {
            BuildConcatAndSliceStage(newBson, maxMessages),
            BuildCharTrimStage(maxTotal, dropCap)
        };

        var filter = Builders<SessionDocument>.Filter.Eq(x => x.Id, sessionKey);
        var update = Builders<SessionDocument>.Update.Pipeline(pipeline);

        return _collection.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    public Task ClearSessionAsync(string sessionKey, CancellationToken cancellationToken = default) =>
        _collection.DeleteOneAsync(x => x.Id == sessionKey, cancellationToken);

    private static BsonDocument StoredMessageToBson(StoredMessage m) =>
        new()
        {
            { nameof(StoredMessage.Role), m.Role },
            { nameof(StoredMessage.Content), m.Content },
            { nameof(StoredMessage.TimestampUtc), m.TimestampUtc }
        };

    private static BsonDocument BuildConcatAndSliceStage(BsonValue newMessageDoc, int maxMessages) =>
        new("$set", new BsonDocument("Messages",
            new BsonDocument("$slice", new BsonArray
            {
                new BsonDocument("$concatArrays", new BsonArray
                {
                    new BsonDocument("$ifNull", new BsonArray { "$Messages", new BsonArray() }),
                    new BsonArray { newMessageDoc }
                }),
                -maxMessages
            })));

    private static BsonDocument BuildCharTrimStage(int maxTotalChars, int dropIterationsCap) =>
        new("$set", new BsonDocument("Messages",
            new BsonDocument("$let", new BsonDocument
            {
                { "vars", new BsonDocument("m", "$Messages") },
                {
                    "in", new BsonDocument("$reduce", new BsonDocument
                    {
                        { "input", new BsonDocument("$range", new BsonArray { 0, dropIterationsCap }) },
                        { "initialValue", "$$m" },
                        {
                            "in", new BsonDocument("$cond", new BsonArray
                            {
                                new BsonDocument("$lte", new BsonArray
                                {
                                    new BsonDocument("$sum", new BsonDocument("$map", new BsonDocument
                                    {
                                        { "input", "$$value" },
                                        { "as", "x" },
                                        {
                                            "in", new BsonDocument("$strLenCP", new BsonDocument("$toString",
                                                new BsonDocument("$ifNull", new BsonArray { "$$x.Content", "" })))
                                        }
                                    })),
                                    maxTotalChars
                                }),
                                "$$value",
                                new BsonDocument("$slice", new BsonArray
                                {
                                    "$$value",
                                    1,
                                    new BsonDocument("$max", new BsonArray
                                    {
                                        0,
                                        new BsonDocument("$subtract", new BsonArray
                                        {
                                            new BsonDocument("$size", "$$value"),
                                            1
                                        })
                                    })
                                })
                            })
                        }
                    })
                }
            })));

    private sealed class SessionDocument
    {
        [BsonId]
        public string Id { get; set; } = "";

        public List<StoredMessage> Messages { get; set; } = [];
    }

    private sealed class StoredMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime TimestampUtc { get; set; }
    }
}
