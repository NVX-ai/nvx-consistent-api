# Strong Id
The name given to the stream id in the framework, a reference to strongly typed IDs.

```cs
public abstract record StrongId
{
  public abstract string StreamId();
  public abstract override string ToString();
}
```

For backwards compatibility with earlier versions of the framework, where all streams were identified by a string, there's a `StrongString` and a `StrongGuid` implementation, they are **not to be used** every entity should have its own strong Id.
