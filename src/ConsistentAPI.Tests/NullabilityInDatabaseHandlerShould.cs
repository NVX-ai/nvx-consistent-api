namespace ConsistentAPI.Tests;

public class NullabilityInDatabaseHandlerShould
{
  [Theory(DisplayName = "Be able to handle null values for all types expected")]
  [InlineData("DATETIME2", typeof(DateTime))]
  [InlineData("DATETIME2", typeof(DateTime?))]
  [InlineData("UNIQUEIDENTIFIER", typeof(Guid))]
  [InlineData("UNIQUEIDENTIFIER", typeof(Guid?))]
  [InlineData("DATETIMEOFFSET", typeof(DateTimeOffset))]
  [InlineData("DATETIMEOFFSET", typeof(DateTimeOffset?))]
  public void Test1(string expectation, Type type) =>
    Assert.Equal(expectation, DatabaseHandler<UserNotificationReadModel>.MapToSqlType(type, ""));
}
