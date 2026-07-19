using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyQOTD.Events;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(QOTDApiServedEvent))]
[JsonSerializable(typeof(QOTDServedEvent))]
[JsonSerializable(typeof(QuoteAddedEvent))]
[JsonSerializable(typeof(RandomQuoteServedEvent))]
[JsonSerializable(typeof(QOTDServiceStartedEvent))]
internal sealed partial class QOTDJsonContext
    : JsonSerializerContext;