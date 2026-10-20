namespace DesktopPet.Services;

/// <summary>
/// Deterministic recurrence calculation engine supporting Daily, Weekdays (Mon-Fri),
/// Weekly (multi-day), and Monthly (clamped day-of-month) recurrence patterns,
/// with robust handling of leap years, month boundaries, timezone preservation, and catch-up rules.
/// </summary>
public static class RecurrenceEngine
{
    /// <summary>
    /// Calculates the first valid occurrence date for a schedule on or after <paramref name="fromLocalDate"/>.
    /// </summary>
    public static DateTime GetFirstOccurrenceDate(RecurringSchedule schedule, DateTime fromLocalDate)
    {
        var baseDate = fromLocalDate.Date;
        bool timePassed = false;
        if (TimeSpan.TryParse(schedule.ScheduledTime, out var scheduledTime))
        {
            timePassed = fromLocalDate.TimeOfDay > scheduledTime;
        }

        return schedule.RecurrenceRule switch
        {
            TaskRecurrenceRule.Daily =>
                timePassed ? baseDate.AddDays(1) : baseDate,

            TaskRecurrenceRule.Weekdays =>
                (baseDate.DayOfWeek == DayOfWeek.Saturday || baseDate.DayOfWeek == DayOfWeek.Sunday)
                    ? GetNextWeekday(baseDate)
                    : (timePassed ? GetNextWeekday(baseDate) : baseDate),

            TaskRecurrenceRule.Weekly => GetFirstWeeklyOccurrence(schedule, fromLocalDate, timePassed),

            TaskRecurrenceRule.Monthly => GetFirstMonthlyOccurrence(schedule, fromLocalDate, timePassed),

            _ => timePassed ? baseDate.AddDays(1) : baseDate
        };
    }

    /// <summary>
    /// Calculates the next occurrence date strictly after <paramref name="fromLocalDate"/>.
    /// </summary>
    public static DateTime CalculateNextOccurrenceDate(RecurringSchedule schedule, DateTime fromLocalDate)
    {
        var baseDate = fromLocalDate.Date;

        return schedule.RecurrenceRule switch
        {
            TaskRecurrenceRule.Daily => baseDate.AddDays(1),

            TaskRecurrenceRule.Weekdays => GetNextWeekday(baseDate),

            TaskRecurrenceRule.Weekly => GetNextWeeklyOccurrence(schedule, baseDate),

            TaskRecurrenceRule.Monthly => GetNextMonthlyOccurrence(schedule, baseDate),

            _ => baseDate.AddDays(1)
        };
    }

    private static DateTime GetNextWeekday(DateTime date)
    {
        var cur = date.AddDays(1);
        while (cur.DayOfWeek == DayOfWeek.Saturday || cur.DayOfWeek == DayOfWeek.Sunday)
        {
            cur = cur.AddDays(1);
        }
        return cur;
    }

    private static DateTime GetFirstWeeklyOccurrence(RecurringSchedule schedule, DateTime fromLocalDate, bool timePassed)
    {
        var baseDate = fromLocalDate.Date;
        var days = GetNormalizedDaysOfWeek(schedule, baseDate);

        // If today matches one of the target days and its scheduled time has not passed, use today
        if (days.Contains(baseDate.DayOfWeek) && !timePassed)
        {
            return baseDate;
        }

        return GetNextWeeklyOccurrence(schedule, baseDate);
    }

    private static DateTime GetNextWeeklyOccurrence(RecurringSchedule schedule, DateTime baseDate)
    {
        var days = GetNormalizedDaysOfWeek(schedule, baseDate);

        DateTime best = DateTime.MaxValue;
        foreach (var dow in days)
        {
            int daysUntil = ((int)dow - (int)baseDate.DayOfWeek + 7) % 7;
            if (daysUntil == 0) daysUntil = 7; // strictly next occurrence
            var candidate = baseDate.AddDays(daysUntil);
            if (candidate < best)
            {
                best = candidate;
            }
        }
        return best;
    }

    private static List<DayOfWeek> GetNormalizedDaysOfWeek(RecurringSchedule schedule, DateTime fallbackDate)
    {
        if (schedule.DaysOfWeek != null && schedule.DaysOfWeek.Count > 0)
        {
            return schedule.DaysOfWeek.Distinct().ToList();
        }
        return new List<DayOfWeek> { fallbackDate.DayOfWeek };
    }

    private static DateTime GetFirstMonthlyOccurrence(RecurringSchedule schedule, DateTime fromLocalDate, bool timePassed)
    {
        var baseDate = fromLocalDate.Date;
        int targetDay = Math.Clamp(schedule.DayOfMonth, 1, 31);
        int maxDaysThisMonth = DateTime.DaysInMonth(baseDate.Year, baseDate.Month);
        int clampedDayThisMonth = Math.Min(targetDay, maxDaysThisMonth);

        if (baseDate.Day < clampedDayThisMonth || (baseDate.Day == clampedDayThisMonth && !timePassed))
        {
            return new DateTime(baseDate.Year, baseDate.Month, clampedDayThisMonth);
        }

        return GetNextMonthlyOccurrence(schedule, baseDate);
    }

    private static DateTime GetNextMonthlyOccurrence(RecurringSchedule schedule, DateTime baseDate)
    {
        int targetDay = Math.Clamp(schedule.DayOfMonth, 1, 31);
        int year = baseDate.Year;
        int month = baseDate.Month + 1;

        if (month > 12)
        {
            month = 1;
            year++;
        }

        int maxDays = DateTime.DaysInMonth(year, month);
        int clampedDay = Math.Min(targetDay, maxDays);
        return new DateTime(year, month, clampedDay);
    }

    /// <summary>
    /// Computes local due date/time and UTC reminder timestamp anchored to local time and timezone.
    /// </summary>
    public static void CalculateOccurrenceDueAndReminder(
        RecurringSchedule schedule,
        DateTime occurrenceLocalDate,
        out DateTime dueDateLocal,
        out DateTime? reminderAtUtc)
    {
        if (!TimeSpan.TryParse(schedule.ScheduledTime, out var timeOfDay))
        {
            timeOfDay = new TimeSpan(9, 0, 0);
        }

        dueDateLocal = occurrenceLocalDate.Date.Add(timeOfDay);

        TimeZoneInfo tz;
        try
        {
            tz = string.IsNullOrWhiteSpace(schedule.TimeZoneId)
                ? TimeZoneInfo.Local
                : TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        }
        catch
        {
            tz = TimeZoneInfo.Local;
        }

        DateTime dueUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dueDateLocal, DateTimeKind.Unspecified), tz);

        if (schedule.ReminderMinutesBefore.HasValue)
        {
            reminderAtUtc = dueUtc.AddMinutes(-schedule.ReminderMinutesBefore.Value);
        }
        else
        {
            reminderAtUtc = null;
        }
    }
}
