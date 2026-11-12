using Fotoprint.Core.Data;

namespace Fotoprint.Tests;

public class MeetingSearchQueryParserTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc); // 12:00 MSK

    [Fact]
    public void Parse_YesterdayQuery_ExtractsYesterdayMoscowDate()
    {
        var result = MeetingSearchQueryParser.Parse("Что было на вчерашнем созвоне?", FixedUtcNow);
        Assert.Equal(new DateOnly(2026, 11, 11), result.TargetDate);
        Assert.False(result.HasTopicTokens);
    }

    [Fact]
    public void Parse_TodayQuery_ExtractsTodayMoscowDate()
    {
        var result = MeetingSearchQueryParser.Parse("Итоги сегодняшнего созвона", FixedUtcNow);
        Assert.Equal(new DateOnly(2026, 11, 12), result.TargetDate);
    }

    [Fact]
    public void Parse_RussianMonthDate_ExtractsDateAndTopic()
    {
        var result = MeetingSearchQueryParser.Parse("О чём говорили на созвоне 10 ноября про каталоги?", FixedUtcNow);
        Assert.Equal(new DateOnly(2026, 11, 10), result.TargetDate);
        Assert.True(result.HasTopicTokens);
    }

    [Fact]
    public void Parse_DotDate_ExtractsDateWithoutTopic()
    {
        var result = MeetingSearchQueryParser.Parse("Пришли протокол встречи 05.11", FixedUtcNow);
        Assert.Equal(new DateOnly(2026, 11, 5), result.TargetDate);
        Assert.False(result.HasTopicTokens);
    }

    [Fact]
    public void Parse_LatestMeetingQuery_SetsPreferLatest()
    {
        var result = MeetingSearchQueryParser.Parse("Покажи последний созвон", FixedUtcNow);
        Assert.Null(result.TargetDate);
        Assert.True(result.PreferLatest);
        Assert.False(result.HasTopicTokens);
    }

    [Fact]
    public void Parse_TopicOnlyQuery_HasTopicTokensOnly()
    {
        var result = MeetingSearchQueryParser.Parse("Какую скидку согласовали для Альфа-Маркет?", FixedUtcNow);
        Assert.Null(result.TargetDate);
        Assert.False(result.PreferLatest);
        Assert.True(result.HasTopicTokens);
    }
}

