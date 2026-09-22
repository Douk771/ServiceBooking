using FluentAssertions;
using ServiceBooking.TestKit;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Покрывает чистую функцию <see cref="TestRunKey.Normalize"/> (§67 ARCHITECTURE_CYCLE8.md).
/// <see cref="TestRunKey.Current"/> сама не тестируется юнит-тестом: она читает переменную
/// окружения процесса один раз статически при первом обращении, и повторная инициализация
/// в рамках одного прогона недостижима — это осознанное ограничение дизайна (см. §67).
/// </summary>
public class TestRunKeyTests
{
    [Theory]
    [InlineData("a3f19c7b", "a3f19c7b")]
    [InlineData("A3F19C7B", "a3f19c7b")]
    [InlineData("  a3f19c7b  ", "a3f19c7b")]
    public void Normalize_accepts_valid_keys_and_lowercases_them(string input, string expected)
    {
        TestRunKey.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzzzzzzz")]
    [InlineData("a3f19c7")]
    [InlineData("a3f19c7bb")]
    [InlineData("a3f19c7b; DROP DATABASE servicebooking")]
    public void Normalize_rejects_malformed_keys(string input)
    {
        var act = () => TestRunKey.Normalize(input);

        act.Should().Throw<TestSafetyException>();
    }
}
