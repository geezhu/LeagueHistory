namespace League.Models;

/// <summary>The estimated score of a match's own player and the MVP or SVP mark it earns.</summary>
public readonly record struct OwnMatchScore(double Estimate, MatchHonour Honour);
