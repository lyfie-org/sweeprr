namespace Sweeprr.API.Dtos.Media;

/// <summary><c>Queued</c> items were newly added to the Sweep Queue as Pending.
/// <c>AlreadyQueued</c> items already had a Pending entry and were left untouched.</summary>
public sealed record QueueManualResponse(int Queued, int AlreadyQueued);
