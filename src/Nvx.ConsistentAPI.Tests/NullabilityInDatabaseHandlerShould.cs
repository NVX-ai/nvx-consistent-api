using System.Reflection;

namespace Nvx.ConsistentAPI.Tests;

public class NullabilityInDatabaseHandlerShould
{
  public static TheoryData<string, PropertyInfo> TestData =>
    new()
    {
      { "DATETIME2", typeof(FieldsTest).GetProperty(nameof(FieldsTest.DateTime))! },
      { "DATETIME2", typeof(FieldsTest).GetProperty(nameof(FieldsTest.NullableDateTime))! },
      { "UNIQUEIDENTIFIER", typeof(FieldsTest).GetProperty(nameof(FieldsTest.Guid))! },
      { "UNIQUEIDENTIFIER", typeof(FieldsTest).GetProperty(nameof(FieldsTest.NullableGuid))! },
      { "DATETIMEOFFSET", typeof(FieldsTest).GetProperty(nameof(FieldsTest.DateTimeOffset))! },
      { "DATETIMEOFFSET", typeof(FieldsTest).GetProperty(nameof(FieldsTest.NullableDateTimeOffset))! }
    };

  [Theory(DisplayName = "Be able to handle null values for all types expected")]
#pragma warning disable xUnit1045
  [MemberData(nameof(TestData))]
#pragma warning restore xUnit1045
  public void Test1(string expectation, PropertyInfo propertyInfo) =>
    Assert.Equal(expectation, DatabaseHandler<UserNotificationReadModel>.MapToSqlType(propertyInfo));
}

public record FieldsTest(
  DateTime DateTime,
  DateTime? NullableDateTime,
  Guid Guid,
  Guid? NullableGuid,
  DateTimeOffset DateTimeOffset,
  DateTimeOffset? NullableDateTimeOffset);
