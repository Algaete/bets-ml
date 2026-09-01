using CornersPrediction.Web.Models.BotPicks;

namespace CornersPrediction.Web.Services;

public static class BotPickDefaultDateRange
{
    public static void Apply(BotPickFiltersViewModel filters, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var currentDate = today.Date;
        if (!filters.DateFrom.HasValue && !filters.DateTo.HasValue)
        {
            var currentMonthStart = new DateTime(currentDate.Year, currentDate.Month, 1);
            var currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);
            var upcomingEnd = currentDate.AddDays(7);

            filters.DateFrom = currentMonthStart;
            filters.DateTo = upcomingEnd > currentMonthEnd ? upcomingEnd : currentMonthEnd;
            return;
        }

        // A boundary supplied in the URL is an explicit user filter. Preserve
        // it and only complete the missing side with the previous month rule.
        var referenceDate = filters.DateFrom ?? filters.DateTo ?? currentDate;
        var monthStart = new DateTime(referenceDate.Year, referenceDate.Month, 1);
        filters.DateFrom ??= monthStart;
        filters.DateTo ??= monthStart.AddMonths(1).AddDays(-1);
    }
}
