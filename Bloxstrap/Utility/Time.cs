namespace Bloxstrap.Utility
{
    internal static class Time
    {
        public static string FormatTimeSpan(TimeSpan timeSpan)
        {
            var components = new List<Tuple<int, string, string>>();

            if ((int)timeSpan.TotalDays > 0)
                components.Add(Tuple.Create((int)timeSpan.TotalDays, Strings.Common_Day, Strings.Common_Days));

            if (timeSpan.Hours > 0)
                components.Add(Tuple.Create(timeSpan.Hours, Strings.Common_Hour, Strings.Common_Hours));

            if (timeSpan.Minutes > 0)
                components.Add(Tuple.Create(timeSpan.Minutes, Strings.Common_Minute, Strings.Common_Minutes));

            if (components.Count == 0)
                return $"0 {Strings.Common_Minutes}";

            Func<Tuple<int, string, string>, string> formatter = t =>
                $"{t.Item1} {(t.Item1 == 1 ? t.Item2 : t.Item3)}";

            if (components.Count == 1)
                return formatter(components[0]);

            string lastComponent = formatter(components.Last());
            string otherComponents = string.Join(", ", components.Take(components.Count - 1).Select(formatter));

            return $"{otherComponents} {Strings.Common_And} {lastComponent}";
        }
    }
}