/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyQOTD.Quotes;
using System.Text.Json.Serialization;

namespace HappyQOTD.Events;

// HTTP API request and response types
[JsonSerializable(typeof(CreateQuoteRequest))]
[JsonSerializable(typeof(Quote))]
[JsonSerializable(typeof(List<CreateQuoteRequest>))]
[JsonSerializable(typeof(List<Quote>))]
[JsonSerializable(typeof(SetDailyQuoteRequest))]

// Mission Control event types
[JsonSerializable(typeof(QuoteDeletedEvent))]
[JsonSerializable(typeof(QOTDApiServedEvent))]
[JsonSerializable(typeof(QOTDServedEvent))]
[JsonSerializable(typeof(QuoteAddedEvent))]
[JsonSerializable(typeof(RandomQuoteServedEvent))]
[JsonSerializable(typeof(QOTDServiceStartedEvent))]
internal sealed partial class QOTDJsonContext : JsonSerializerContext;
