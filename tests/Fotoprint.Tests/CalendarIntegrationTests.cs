using System.Text.Json;
using Fotoprint.Core.Agents;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Data;
using Fotoprint.Core.Models;
using Fotoprint.Core.Security;
using Xunit;

namespace Fotoprint.Tests;

public class CalendarIntegrationTests
{
    private static readonly TimeZoneInfo MskZone = FotoprintRepository.GetMoscowTimeZone();

    [Fact]
    public void FindFreeSlots_Generates_Available_Slots_Across_Working_Day()
    {
        // Дата завтра — рабочий день свободен с 09:00 до 18:00
        var nowUtc = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
        var targetDate = new DateOnly(2026, 11, 12);
        var existingMeetings = new List<Meeting>();

        var slots = FotoprintRepository.CalculateFreeSlots(
            existingMeetings,
            targetDate,
            durationMinutes: 30,
            startWork: new TimeOnly(9, 0),
            endWork: new TimeOnly(18, 0),
            bufferMinutes: 10,
            nowUtc: nowUtc);

        Assert.NotEmpty(slots);
        // С 09:00 до 18:00 с шагом 30 мин ровно 18 слотов
        Assert.Equal(18, slots.Count);
        Assert.Equal("09:00 - 09:30", slots.First().Label);
        Assert.Equal("17:30 - 18:00", slots.Last().Label);
    }

    [Fact]
    public void FindFreeSlots_PastDate_Returns_No_Slots()
    {
        // Дата в прошлом относительно nowUtc
        var nowUtc = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc); // 13:00 MSK 12 ноября
        var pastDate = new DateOnly(2026, 11, 11);
        var existingMeetings = new List<Meeting>();

        var slots = FotoprintRepository.CalculateFreeSlots(
            existingMeetings,
            pastDate,
            durationMinutes: 30,
            startWork: new TimeOnly(9, 0),
            endWork: new TimeOnly(18, 0),
            bufferMinutes: 10,
            nowUtc: nowUtc);

        Assert.Empty(slots);
    }

    [Fact]
    public void FindFreeSlots_Today_Cutoff_Excludes_Slots_Before_Now_Plus_5_Minutes()
    {
        // Текущее время: 12 ноября 2026 года, 14:00 по Москве (11:00 UTC)
        var nowUtc = new DateTime(2026, 11, 12, 11, 0, 0, DateTimeKind.Utc);
        var today = new DateOnly(2026, 11, 12);
        var existingMeetings = new List<Meeting>();

        var slots = FotoprintRepository.CalculateFreeSlots(
            existingMeetings,
            today,
            durationMinutes: 30,
            startWork: new TimeOnly(9, 0),
            endWork: new TimeOnly(18, 0),
            bufferMinutes: 10,
            nowUtc: nowUtc);

        Assert.NotEmpty(slots);
        // 14:00 + 5 мин технологический запас = 14:05.
        // Ближайший выровненный 30-минутный интервал — 14:30.
        // Все слоты до 14:05 (09:00 - 14:00) строго исключены!
        var firstSlot = slots.First();
        Assert.Equal("14:30 - 15:00", firstSlot.Label);
        Assert.DoesNotContain(slots, s => s.Label.StartsWith("09:"));
        Assert.DoesNotContain(slots, s => s.Label.StartsWith("10:"));
        Assert.DoesNotContain(slots, s => s.Label.StartsWith("11:"));
        Assert.DoesNotContain(slots, s => s.Label.StartsWith("12:"));
        Assert.DoesNotContain(slots, s => s.Label.StartsWith("13:"));
        Assert.DoesNotContain(slots, s => s.Label.StartsWith("14:00"));
    }

    [Fact]
    public void FindFreeSlots_Today_After_Working_Hours_Returns_No_Slots()
    {
        // Текущее время: 12 ноября 2026 года, 18:15 по Москве (15:15 UTC)
        var nowUtc = new DateTime(2026, 11, 12, 15, 15, 0, DateTimeKind.Utc);
        var today = new DateOnly(2026, 11, 12);

        var slots = FotoprintRepository.CalculateFreeSlots(
            new List<Meeting>(),
            today,
            durationMinutes: 30,
            startWork: new TimeOnly(9, 0),
            endWork: new TimeOnly(18, 0),
            bufferMinutes: 10,
            nowUtc: nowUtc);

        Assert.Empty(slots);
    }

    [Fact]
    public void FindFreeSlots_Applies_10_Minute_Buffer_Around_Existing_Meeting()
    {
        // Дата: 13 ноября 2026 (завтра), текущее время утро 08:00 MSK (05:00 UTC)
        var nowUtc = new DateTime(2026, 11, 12, 5, 0, 0, DateTimeKind.Utc);
        var targetDate = new DateOnly(2026, 11, 13);

        // Встреча: 14:00 – 15:00 MSK (11:00 – 12:00 UTC)
        var meetingStartUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 13, 14, 0, 0), MskZone);
        var meetingEndUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 13, 15, 0, 0), MskZone);

        var existingMeetings = new List<Meeting>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Совещание по браку Heidelberg",
                StartTime = meetingStartUtc,
                EndTime = meetingEndUtc,
                Status = "scheduled",
                OrganizerId = 1,
                ParticipantIds = new List<int> { 2 }
            }
        };

        var slots = FotoprintRepository.CalculateFreeSlots(
            existingMeetings,
            targetDate,
            durationMinutes: 30,
            startWork: new TimeOnly(9, 0),
            endWork: new TimeOnly(18, 0),
            bufferMinutes: 10,
            nowUtc: nowUtc);

        // Проверяем буфер:
        // 1. Слот 13:00 - 13:30 свободен (заканчивается в 13:30, до 13:50)
        Assert.Contains(slots, s => s.Label == "13:00 - 13:30");

        // 2. Слот 13:30 - 14:00 ИСКЛЮЧЕН (заканчивается в 14:00, попадает в 10-мин буфер 13:50-14:00)
        Assert.DoesNotContain(slots, s => s.Label == "13:30 - 14:00");

        // 3. Слоты во время встречи (14:00 - 14:30 и 14:30 - 15:00) ИСКЛЮЧЕНЫ
        Assert.DoesNotContain(slots, s => s.Label == "14:00 - 14:30");
        Assert.DoesNotContain(slots, s => s.Label == "14:30 - 15:00");

        // 4. Слот 15:00 - 15:30 ИСКЛЮЧЕН (начинается в 15:00, попадает в 10-мин буфер 15:00-15:10)
        Assert.DoesNotContain(slots, s => s.Label == "15:00 - 15:30");

        // 5. Слот 15:30 - 16:00 свободен (начинается в 15:30, после 15:10)
        Assert.Contains(slots, s => s.Label == "15:30 - 16:00");
    }

    [Fact]
    public async Task BuildSlotSelectionActionCard_Produces_Exact_Schema()
    {
        var fakeRepo = new FakeCalendarRepo();
        var agent = new FotoprintAiAgent(
            gemini: null!,
            repo: fakeRepo,
            httpClientFactory: null!,
            config: new FotoprintConfig());

        var toolArgsJson = """
        {
            "organizer_employee_id": 1,
            "participant_employee_ids": [2],
            "date": "2026-11-12",
            "duration_minutes": 30
        }
        """;
        var toolArgs = JsonSerializer.Deserialize<JsonElement>(toolArgsJson);

        var toolResultJson = """
        {
            "status": "success",
            "date": "2026-11-12",
            "duration_minutes": 30,
            "available_slots": [
                {
                    "start_time": "2026-11-12T14:30:00+03:00",
                    "end_time": "2026-11-12T15:00:00+03:00",
                    "label": "14:30 - 15:00"
                },
                {
                    "start_time": "2026-11-12T16:00:00+03:00",
                    "end_time": "2026-11-12T16:30:00+03:00",
                    "label": "16:00 - 16:30"
                }
            ],
            "is_today": true,
            "past_slots_cutoff": "14:00",
            "has_free_slots": true
        }
        """;
        var toolResult = JsonSerializer.Deserialize<JsonElement>(toolResultJson);

        var cardObj = await agent.BuildSlotSelectionActionCardAsync(toolArgs, toolResult, CancellationToken.None);
        Assert.NotNull(cardObj);

        var cardJson = JsonSerializer.Serialize(cardObj);
        using var doc = JsonDocument.Parse(cardJson);
        var root = doc.RootElement;

        Assert.Equal("slot_selection", root.GetProperty("type").GetString());
        Assert.Equal("Кузнецов Иван Петрович", root.GetProperty("target_person").GetString());
        Assert.Equal(2, root.GetProperty("target_employee_id").GetInt32());
        Assert.Equal("2026-11-12", root.GetProperty("date").GetString());
        Assert.Equal(30, root.GetProperty("duration_minutes").GetInt32());

        var slots = root.GetProperty("slots");
        Assert.Equal(2, slots.GetArrayLength());
        Assert.Equal("slot_1", slots[0].GetProperty("id").GetString());
        Assert.Equal("14:30", slots[0].GetProperty("start").GetString());
        Assert.Equal("15:00", slots[0].GetProperty("end").GetString());
        Assert.Equal("2026-11-12T14:30:00+03:00", slots[0].GetProperty("iso_start").GetString());
        Assert.Equal("2026-11-12T15:00:00+03:00", slots[0].GetProperty("iso_end").GetString());

        var durationButtons = root.GetProperty("duration_buttons");
        Assert.Equal(4, durationButtons.GetArrayLength());
        Assert.True(durationButtons[1].GetProperty("is_default").GetBoolean());
    }

    [Fact]
    public void FindFreeSlots_15Minute_Duration_Generates_Quarter_Hour_Slots()
    {
        var nowUtc = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
        var targetDate = new DateOnly(2026, 11, 12);

        var slots = FotoprintRepository.CalculateFreeSlots(
            new List<Meeting>(),
            targetDate,
            durationMinutes: 15,
            startWork: new TimeOnly(9, 0),
            endWork: new TimeOnly(18, 0),
            bufferMinutes: 10,
            nowUtc: nowUtc);

        Assert.NotEmpty(slots);
        Assert.Equal("09:00 - 09:15", slots.First().Label);
        Assert.Contains(slots, s => s.Label == "09:15 - 09:30");
        Assert.Contains(slots, s => s.Label == "09:30 - 09:45");
        Assert.Contains(slots, s => s.Label == "09:45 - 10:00");
    }

    [Fact]
    public void FindFreeSlots_Multiple_Meetings_Generates_Windows_Between_Them()
    {
        var nowUtc = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
        var targetDate = new DateOnly(2026, 11, 12);

        // Две встречи: 10:00-11:00 и 15:00-16:00 MSK
        var m1StartUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 12, 10, 0, 0), MskZone);
        var m1EndUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 12, 11, 0, 0), MskZone);
        var m2StartUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 12, 15, 0, 0), MskZone);
        var m2EndUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 12, 16, 0, 0), MskZone);

        var meetings = new List<Meeting>
        {
            new() { Id = Guid.NewGuid(), StartTime = m1StartUtc, EndTime = m1EndUtc, Status = "scheduled" },
            new() { Id = Guid.NewGuid(), StartTime = m2StartUtc, EndTime = m2EndUtc, Status = "scheduled" }
        };

        var slots = FotoprintRepository.CalculateFreeSlots(
            meetings,
            targetDate,
            durationMinutes: 30,
            startWork: new TimeOnly(9, 0),
            endWork: new TimeOnly(18, 0),
            bufferMinutes: 10,
            nowUtc: nowUtc);

        // Окно между 11:10 и 14:50 доступно:
        Assert.Contains(slots, s => s.Label == "11:30 - 12:00");
        Assert.Contains(slots, s => s.Label == "12:00 - 12:30");
        Assert.Contains(slots, s => s.Label == "13:00 - 13:30");
        Assert.Contains(slots, s => s.Label == "14:00 - 14:30");

        // Окна во время встреч или буферов исключены:
        Assert.DoesNotContain(slots, s => s.Label == "10:00 - 10:30");
        Assert.DoesNotContain(slots, s => s.Label == "10:30 - 11:00");
        Assert.DoesNotContain(slots, s => s.Label == "11:00 - 11:30"); // 11:00-11:10 buffer
        Assert.DoesNotContain(slots, s => s.Label == "14:30 - 15:00"); // 14:50-15:00 buffer
        Assert.DoesNotContain(slots, s => s.Label == "15:00 - 15:30");
        Assert.DoesNotContain(slots, s => s.Label == "15:30 - 16:00");
        Assert.DoesNotContain(slots, s => s.Label == "16:00 - 16:30"); // 16:00-16:10 buffer
    }

    [Fact]
    public void FindFreeSlots_Zero_Buffer_Allows_Adjacent_Slots()
    {
        var nowUtc = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
        var targetDate = new DateOnly(2026, 11, 12);

        // Встреча: 14:00 – 15:00 MSK
        var mStartUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 12, 14, 0, 0), MskZone);
        var mEndUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 12, 15, 0, 0), MskZone);

        var meetings = new List<Meeting>
        {
            new() { Id = Guid.NewGuid(), StartTime = mStartUtc, EndTime = mEndUtc, Status = "scheduled" }
        };

        var slots = FotoprintRepository.CalculateFreeSlots(
            meetings,
            targetDate,
            durationMinutes: 30,
            startWork: new TimeOnly(9, 0),
            endWork: new TimeOnly(18, 0),
            bufferMinutes: 0,
            nowUtc: nowUtc);

        // С нулевым буфером ровно в стык: 13:30-14:00 и 15:00-15:30 свободны
        Assert.Contains(slots, s => s.Label == "13:30 - 14:00");
        Assert.Contains(slots, s => s.Label == "15:00 - 15:30");
        // Но внутри встречи исключены:
        Assert.DoesNotContain(slots, s => s.Label == "14:00 - 14:30");
        Assert.DoesNotContain(slots, s => s.Label == "14:30 - 15:00");
    }

    [Fact]
    public void CalendarEventItem_DomainVocabulary_And_CategoryMatching()
    {
        // 1. Reference week demo items must have exact professional typography title
        var demoItems = CalendarHelper.GetReferenceWeekDemoItems(MskZone, id => $"Сотрудник {id}");
        Assert.NotEmpty(demoItems);
        
        var routineMorningItems = demoItems.Where(i => i.Id.StartsWith("b0000001-")).ToList();
        Assert.Equal(7, routineMorningItems.Count);
        foreach (var item in routineMorningItems)
        {
            Assert.Equal("Утренняя сводка выработки цеха", item.Title);
            Assert.Equal("routine", item.Category);
            Assert.Equal("routine", item.ItemType);
            Assert.True(item.IsAllDay);
        }

        // Domain invariant: Zero domestic chore stems across all demo items
        string[] forbiddenStems = ["поощрен", "уборк", "упражнен", "выпить вод"];
        foreach (var item in demoItems)
        {
            var combinedText = $"{item.Title} {item.Description} {string.Join(" ", item.Tags)}".ToLowerInvariant();
            foreach (var stem in forbiddenStems)
            {
                Assert.DoesNotContain(stem, combinedText);
            }
        }

        // 2. Keyword category matching: "выработка" and "сводка" map to "routine"
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            Title = "Ежедневная выработка цеха офсетной печати",
            Description = "Анализ тиражей и брака за смену",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1),
            Status = "scheduled"
        };
        var eventItem = CalendarHelper.MapMeetingToCalendarItem(meeting, MskZone, id => $"Сотрудник {id}");
        Assert.Equal("routine", eventItem.Category);
    }

    private class FakeCalendarRepo : IFotoprintRepository
    {
        public Task<Employee?> GetEmployeeByIdAsync(int id, CancellationToken ct = default)
        {
            if (id == 2)
            {
                return Task.FromResult<Employee?>(new Employee
                {
                    Id = 2,
                    FirstName = "Иван",
                    LastName = "Кузнецов",
                    MiddleName = "Петрович",
                    Department = "Производство",
                    Position = "Начальник производства"
                });
            }
            return Task.FromResult<Employee?>(null);
        }

        public Task<EmployeeSecurityContext?> GetEmployeeSecurityContextAsync(long telegramId, CancellationToken ct = default) => Task.FromResult<EmployeeSecurityContext?>(null);
        public Task<EmployeeSecurityContext?> GetEmployeeSecurityContextByMasterIdAsync(long masterId, CancellationToken ct = default) => Task.FromResult<EmployeeSecurityContext?>(null);
        public Task<long?> ResolveMasterIdByMessengerAsync(string messengerType, string messengerUserId, CancellationToken ct = default) => Task.FromResult<long?>(null);
        public Task<List<Employee>> GetAllEmployeesAsync(CancellationToken ct = default) => Task.FromResult(new List<Employee>());
        public Task<ChatSession> GetOrCreateActiveSessionByMasterIdAsync(long masterId, long messengerChatId, string messengerType = "telegram", CancellationToken ct = default) => Task.FromResult(new ChatSession());
        public Task<ChatSession> SwitchToNewSessionAsync(Guid currentSessionId, long masterId, long messengerChatId, string? newBranch = null, string messengerType = "telegram", CancellationToken ct = default) => Task.FromResult(new ChatSession());
        public Task<List<ChatMessage>> GetMessagesBySessionIdAsync(Guid sessionId, int limit = 20, CancellationToken ct = default) => Task.FromResult(new List<ChatMessage>());
        public Task SaveMessageToSessionAsync(Guid sessionId, long masterId, string role, string content, bool isFrozen = false, object? metadata = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ChatSession> GetOrCreateSessionAsync(long telegramChatId, long? telegramUserId = null, CancellationToken ct = default) => Task.FromResult(new ChatSession());
        public Task SaveMessageAsync(Guid sessionId, string contextId, string role, string content, bool isFrozen = false, object? metadata = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<ChatMessage>> GetRecentMessagesAsync(Guid sessionId, int limit = 20, CancellationToken ct = default) => Task.FromResult(new List<ChatMessage>());
        public Task FreezeContextAsync(Guid sessionId, string contextId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Guid> SaveDocumentAsync(string title, string sourceType, string? filePath, string? department, List<string> allowedAccessGroups, object? metadata = null, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task SaveDocumentChunksAsync(Guid documentId, List<string> chunkTexts, List<float[]?> embeddings, List<string> allowedAccessGroups, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<CorporateDocumentChunk>> SearchSimilarChunksAsync(float[] queryEmbedding, List<string> userAccessGroups, int topK = 5, CancellationToken ct = default) => Task.FromResult(new List<CorporateDocumentChunk>());
        public Task<List<CorporateDocumentChunk>> SearchTextChunksAsync(string queryText, List<string> userAccessGroups, int topK = 5, CancellationToken ct = default) => Task.FromResult(new List<CorporateDocumentChunk>());
        public Task<Guid> CreateMeetingAsync(string title, string? description, DateTime startTime, DateTime? endTime = null, string status = "completed", int? organizerId = null, List<int>? participantIds = null, object? metadata = null, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task<List<Meeting>> GetUpcomingMeetingsAsync(int timeWindowMinutes = 30, CancellationToken ct = default) => Task.FromResult(new List<Meeting>());
        public Task<List<Meeting>> GetMeetingsForRangeAsync(DateTime fromUtc, DateTime toUtc, int? employeeId = null, CancellationToken ct = default) => Task.FromResult(new List<Meeting>());
        public Task<List<TimeSlot>> FindFreeSlotsAsync(int organizerId, List<int> participantIds, DateOnly targetDate, int durationMinutes, TimeOnly? workingStart = null, TimeOnly? workingEnd = null, int bufferMinutes = 10, CancellationToken ct = default) => Task.FromResult(new List<TimeSlot>());
        public Task<Meeting?> GetMeetingByIdAsync(Guid meetingId, CancellationToken ct = default) => Task.FromResult<Meeting?>(null);
        public Task<List<MeetingNote>> GetMeetingNotesAsync(Guid meetingId, CancellationToken ct = default) => Task.FromResult(new List<MeetingNote>());
        public Task SaveMeetingNoteAsync(Guid meetingId, int? authorId, string noteText, string category = "general", CancellationToken ct = default) => Task.CompletedTask;
        public Task<Guid> SaveMeetingProtocolAsync(Guid meetingId, string title, string summary, List<string> decisions, string? rawTranscript = null, string? docxPath = null, string? pdfPath = null, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task<MeetingProtocol?> GetMeetingProtocolAsync(Guid meetingId, CancellationToken ct = default) => Task.FromResult<MeetingProtocol?>(null);
        public Task<Guid> CreateActionItemAsync(Guid meetingId, Guid? protocolId, string title, string? description, int? responsibleId, DateTime? deadline, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task<List<ActionItem>> GetActionItemsByMeetingAsync(Guid meetingId, CancellationToken ct = default) => Task.FromResult(new List<ActionItem>());
        public Task<List<ActionItem>> GetActionItemsByResponsibleAsync(int employeeId, string? statusFilter = null, CancellationToken ct = default) => Task.FromResult(new List<ActionItem>());
        public Task UpdateActionItemStatusAsync(Guid actionItemId, string status, int? bitrix24TaskId = null, CancellationToken ct = default) => Task.CompletedTask;
    }
}
