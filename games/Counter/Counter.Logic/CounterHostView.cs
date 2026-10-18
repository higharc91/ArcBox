namespace Counter.Logic;

public sealed record CounterRow(string Name, int Count);
public sealed record CounterHostView(IReadOnlyList<CounterRow> Rows, DateTimeOffset? EndsAt);
