using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyQOTD.Quotes;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Quote))]
internal partial class QuoteJsonContext : JsonSerializerContext;