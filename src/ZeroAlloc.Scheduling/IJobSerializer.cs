namespace ZeroAlloc.Scheduling;

/// <summary>Serialises and deserialises job payloads to/from bytes.</summary>
public interface IJobSerializer
{
    byte[] Serialize<T>(T job) where T : notnull;
    T Deserialize<T>(byte[] payload) where T : notnull;
}
