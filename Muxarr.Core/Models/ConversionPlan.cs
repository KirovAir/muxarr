namespace Muxarr.Core.Models;

// Converter-facing payload. Delta carries only fields that differ from source
// (null = leave alone). Container quirks are pre-resolved by the planner, so
// converters apply every non-null field verbatim without branching.
public sealed record ConversionPlan(TargetSnapshot Delta, long SourceDurationMs);
