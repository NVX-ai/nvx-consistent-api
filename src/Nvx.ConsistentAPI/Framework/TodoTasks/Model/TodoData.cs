namespace Nvx.ConsistentAPI;

/// <summary>
/// Marker interface for the data payload carried by a todo task.
/// Implementations are serialized to JSON when a todo is created and deserialized when executed.
/// </summary>
public interface TodoData;
