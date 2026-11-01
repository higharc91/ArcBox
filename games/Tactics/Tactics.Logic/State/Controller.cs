using System.Text.Json.Serialization;
using ArcBox.Sdk;

namespace Tactics.Logic;

/// <summary>Who decides a unit's commands: a player's phone or the AI.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PlayerControlled), nameof(PlayerControlled))]
[JsonDerivedType(typeof(AiControlled), nameof(AiControlled))]
public abstract record Controller;

/// <summary>A unit commanded by the player with this profile.</summary>
public sealed record PlayerControlled(ProfileId Owner) : Controller;

/// <summary>A unit commanded by the AI with the named profile (enemies; absent party members use <c>"companion"</c>).</summary>
public sealed record AiControlled(string Profile) : Controller;
