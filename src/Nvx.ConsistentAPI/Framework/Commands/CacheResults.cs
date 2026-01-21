namespace Nvx.ConsistentAPI.Framework.Commands;

public record CacheLockedResult;

public record SuccessCachedResult(CommandAcceptedResult Value);

public record ErrorCacheResult(ApiError Value);

public record CacheLockAvailableResult(long Revision);
