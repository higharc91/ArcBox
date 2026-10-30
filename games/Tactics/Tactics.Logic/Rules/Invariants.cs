namespace Tactics.Logic;

/// <summary>
/// The engine reached a state its rules forbid (for example hooks triggering each other deeper than
/// <see cref="Hooks.MaxDepth"/>). This is a bug in the engine or the content, never a player's mistake.
/// </summary>
public sealed class RulesInvariantException(string message) : Exception(message);
