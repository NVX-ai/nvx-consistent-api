using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using KurrentDB.Client;

namespace Nvx.ConsistentAPI.Tests;

using static IdempotentUuid;

public class IdempotentUuidGenerates
{
  [Property(
    DisplayName = "The same Uuid every time",
    Arbitrary = [typeof(NonEmptyStringGenerator)],
    MaxTest = 50_000
  )]
  public bool T1(string input) =>
    // ReSharper disable once EqualExpressionComparison
    Generate(input) == Generate(input);

  [Property(
    DisplayName = "A non empty Uuid",
    Arbitrary = [typeof(NonEmptyStringGenerator)],
    MaxTest = 50_000
  )]
  public bool T2(string input) =>
    Generate(input) != Uuid.Empty;

  [Property(
    DisplayName = "To string and back works",
    Arbitrary = [typeof(NonEmptyStringGenerator)],
    MaxTest = 50_000
  )]
  public bool T3(string input) =>
    Generate(input) == Uuid.Parse(Generate(input).ToString());
}

public class NonEmptyStringGenerator
{
  // ReSharper disable once UnusedMember.Global
  public static Arbitrary<string> Strings() => ArbMap.Default.ArbFor<string>().Filter(s => !string.IsNullOrEmpty(s));
}
