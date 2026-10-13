using Orders.Infrastructure.Ai;

namespace Orders.UnitTests;

public class PeriodTests
{
    // Brasil: UTC-3 fixo (sem horario de verao desde 2019).
    private static readonly TimeZoneInfo Brt = TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");

    // 2026-10-09 22:30 em Brasilia = 2026-10-10 01:30 UTC: "hoje" ainda e dia 09 para o usuario.
    private static readonly DateTimeOffset Now = TimeZoneInfo.ConvertTime(new DateTimeOffset(2026, 10, 10, 1, 30, 0, TimeSpan.Zero), Brt);

    [Fact]
    public void Hoje_uses_the_users_timezone_not_UTC()
    {
        var (from, to) = OrdersAssistant.ResolvePeriod("hoje", null, null, Brt, Now);

        Assert.Equal(new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.FromHours(-3)), from);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.FromHours(-3)), to);
    }

    [Fact]
    public void Este_mes_covers_the_whole_local_month()
    {
        var (from, to) = OrdersAssistant.ResolvePeriod("este_mes", null, null, Brt, Now);

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-3)), from);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-3)), to);
    }

    [Fact]
    public void Mes_passado_is_the_previous_calendar_month()
    {
        var (from, to) = OrdersAssistant.ResolvePeriod("mes_passado", null, null, Brt, Now);

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(-3)), from);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-3)), to);
    }

    [Fact]
    public void Ultimos_7_dias_includes_today()
    {
        var (from, to) = OrdersAssistant.ResolvePeriod("ultimos_7_dias", null, null, Brt, Now);

        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.FromHours(-3)), from);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.FromHours(-3)), to);
    }

    [Fact]
    public void Explicit_dates_are_inclusive_on_both_ends()
    {
        var (from, to) = OrdersAssistant.ResolvePeriod(null, "2026-10-01", "2026-10-05", Brt, Now);

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-3)), from);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.FromHours(-3)), to); // fim exclusivo = dia seguinte
    }

    [Theory]
    [InlineData(null)]
    [InlineData("todos")]
    [InlineData("qualquer coisa")]
    public void Unknown_or_missing_period_means_no_filter(string? periodo)
    {
        var (from, to) = OrdersAssistant.ResolvePeriod(periodo, null, null, Brt, Now);

        Assert.Null(from);
        Assert.Null(to);
    }
}
